class_name FlowerDissolve
extends Node3D
## 「死亡时像花一样消散」组件。
##
## 用法：把这个节点作为子节点挂到角色下面（或在 target 指定角色根节点），
## 角色死亡时调用 play()。它会：
##   1. 把 target 下所有 MeshInstance3D 换成溶解材质，从上到下（可改）一片片消失；
##   2. 在正在溶解的边缘生成花瓣粒子，花瓣翻滚着被风吹走、落地后缩小消失。
## 信号 dissolve_finished：身体完全消失（此时可以安全 queue_free 角色）。
## 信号 petals_finished：最后一片花瓣也消失了。

signal dissolve_finished
signal petals_finished

const DISSOLVE_SHADER := preload("res://flower_death/dissolve.gdshader")
const PETAL_SHADER := preload("res://flower_death/petal.gdshader")
const NOISE_OFFSET := 4096

## 要消散的角色根节点；留空则使用父节点。
@export var target: Node3D

@export_group("Dissolve")
@export var duration := 2.6
@export var top_down := true
@export_range(0.0, 1.0) var height_weight := 0.65
@export var noise_scale := 5.0
@export_range(0.0, 0.3) var edge_width := 0.05
@export var edge_color := Color(1.0, 0.58, 0.78)
@export var edge_emission := 5.0
@export var hide_target_when_done := true

@export_group("Petals")
@export var petal_amount := 700
@export var petal_lifetime := 3.2
@export var petal_size := Vector2(0.075, 0.1)
## 风（恒定加速度，m/s²）。y 为正时花瓣会先往上飘。
@export var wind := Vector3(1.2, 0.1, 0.3)
@export var petal_colors: Gradient
## 场景里有 GPUParticlesCollision* 节点时，花瓣会落在上面。
@export var petals_collide := true
## 把粒子节点挪到场景根下，这样角色被 queue_free 后花瓣仍会继续飘完。
@export var detach_petals := true
## 在角色表面采样多少个点作为花瓣出生点。
@export var surface_samples := 6000
@export var bands := 48

var is_playing := false

var _meshes: Array[MeshInstance3D] = []
var _saved: Array[Dictionary] = []
var _materials: Array[ShaderMaterial] = []
var _particles: GPUParticles3D
var _pm: ParticleProcessMaterial
var _band_points: Array[ImageTexture] = []
var _band_normals: Array[ImageTexture] = []
var _band_counts: PackedInt32Array = PackedInt32Array()
var _max_band_count := 1
var _v_min := 0.0
var _v_max := 1.0
var _band_w := 1.0
var _cur_band := -999
var _p_start := 0.0
var _p_end := 1.0
var _t := 0.0
var _rng := RandomNumberGenerator.new()


func _ready() -> void:
	set_process(false)


## 开始消散。
func play() -> void:
	if is_playing:
		return
	var root: Node3D = target if target != null else get_parent() as Node3D
	if root == null:
		push_warning("FlowerDissolve: 没有可用的 target。")
		return
	_meshes.clear()
	_collect_meshes(root, _meshes)
	if _meshes.is_empty():
		push_warning("FlowerDissolve: target 下没有 MeshInstance3D。")
		return

	var world_aabb := _world_aabb()
	_apply_dissolve_materials(world_aabb)
	_create_particles()
	_build_emission_bands(world_aabb)

	# 溶解阈值的起止：起点刚好没有任何像素在发光，终点刚好全部消失
	_p_start = _v_min - edge_width - 0.01
	_p_end = _v_max + 0.03
	_set_progress(_p_start)
	_cur_band = -999
	_t = 0.0
	is_playing = true
	_particles.emitting = true
	set_process(true)


## 恢复角色原样（演示/复活用）。已经飞出去的花瓣会继续飘完。
func reset() -> void:
	set_process(false)
	is_playing = false
	_retire_particles()
	for i in _saved.size():
		var s: Dictionary = _saved[i]
		var mi: MeshInstance3D = s["mesh"]
		if not is_instance_valid(mi):
			continue
		mi.material_override = s["override"]
		var surf: Array = s["surface"]
		for j in surf.size():
			mi.set_surface_override_material(j, surf[j])
		mi.visible = s["visible"]
	_saved.clear()
	_materials.clear()


func _process(delta: float) -> void:
	_t += delta
	var k := clampf(_t / duration, 0.0, 1.0)
	# 开头稍慢、结尾稍快，消散更有「被风带走」的感觉
	var eased := k * k * (3.0 - 2.0 * k) * 0.5 + k * 0.5
	var p: float = lerpf(_p_start, _p_end, eased)
	_set_progress(p)
	_update_emission_band(p)
	if k >= 1.0:
		_finish()


func _finish() -> void:
	set_process(false)
	is_playing = false
	if hide_target_when_done:
		for mi in _meshes:
			if is_instance_valid(mi):
				mi.visible = false
	_retire_particles()
	dissolve_finished.emit()
	# 粒子寿命有随机，额外等一点（用场景树计时器，角色被释放也不影响）
	get_tree().create_timer(petal_lifetime * 1.5).timeout.connect(petals_finished.emit)


## 停止发射，等现有花瓣飘完后释放粒子节点。
func _retire_particles() -> void:
	if not is_instance_valid(_particles):
		_particles = null
		return
	var p := _particles
	_particles = null
	p.amount_ratio = 0.0
	p.emitting = false
	p.get_tree().create_timer(petal_lifetime * 1.5).timeout.connect(p.queue_free)


# ------------------------------------------------------------------ 溶解材质

func _collect_meshes(node: Node, out: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D and (node as MeshInstance3D).mesh != null:
		out.append(node)
	for c in node.get_children():
		_collect_meshes(c, out)


func _world_aabb() -> AABB:
	var aabb := AABB()
	var first := true
	for mi in _meshes:
		var a: AABB = mi.global_transform * mi.get_aabb()
		if first:
			aabb = a
			first = false
		else:
			aabb = aabb.merge(a)
	return aabb


func _apply_dissolve_materials(world_aabb: AABB) -> void:
	_saved.clear()
	_materials.clear()
	for mi in _meshes:
		var surf: Array = []
		for s in mi.mesh.get_surface_count():
			surf.append(mi.get_surface_override_material(s))
		_saved.append({"mesh": mi, "override": mi.material_override, "surface": surf, "visible": mi.visible})

		var new_mats: Array[ShaderMaterial] = []
		for s in mi.mesh.get_surface_count():
			var src := mi.get_active_material(s)
			var m := ShaderMaterial.new()
			m.shader = DISSOLVE_SHADER
			if src is BaseMaterial3D:
				var b := src as BaseMaterial3D
				m.set_shader_parameter("albedo", b.albedo_color)
				if b.albedo_texture != null:
					m.set_shader_parameter("albedo_texture", b.albedo_texture)
				m.set_shader_parameter("roughness", b.roughness)
				m.set_shader_parameter("metallic", b.metallic)
			elif src != null:
				push_warning("FlowerDissolve: %s 用的不是 StandardMaterial3D，溶解时会变成默认颜色。可把 dissolve.gdshader 的逻辑合并到你自己的 shader 里。" % mi.name)
			m.set_shader_parameter("min_y", world_aabb.position.y)
			m.set_shader_parameter("max_y", world_aabb.end.y)
			m.set_shader_parameter("top_down", top_down)
			m.set_shader_parameter("height_weight", height_weight)
			m.set_shader_parameter("noise_scale", noise_scale)
			m.set_shader_parameter("edge_width", edge_width)
			m.set_shader_parameter("edge_color", edge_color)
			m.set_shader_parameter("edge_emission", edge_emission)
			new_mats.append(m)
			_materials.append(m)

		mi.material_override = null
		for s in new_mats.size():
			mi.set_surface_override_material(s, new_mats[s])


func _set_progress(p: float) -> void:
	for m in _materials:
		m.set_shader_parameter("progress", p)


# ------------------------------------------------------------------ 花瓣粒子

func _create_particles() -> void:
	_particles = GPUParticles3D.new()
	_particles.name = "Petals"
	_particles.amount = petal_amount
	_particles.lifetime = petal_lifetime
	_particles.local_coords = false
	_particles.emitting = false
	_particles.amount_ratio = 0.0
	_particles.visibility_aabb = AABB(Vector3(-10, -4, -10), Vector3(20, 12, 20))

	var quad := QuadMesh.new()
	quad.size = petal_size
	var pmat := ShaderMaterial.new()
	pmat.shader = PETAL_SHADER
	quad.material = pmat
	_particles.draw_pass_1 = quad

	_pm = ParticleProcessMaterial.new()
	_pm.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_DIRECTED_POINTS
	# 对 DIRECTED_POINTS，direction 的 z 分量对应表面法线方向
	_pm.direction = Vector3(0, 0, 1)
	_pm.spread = 50.0
	_pm.initial_velocity_min = 0.25
	_pm.initial_velocity_max = 0.9
	_pm.gravity = wind + Vector3(0, -0.35, 0)
	_pm.damping_min = 0.3
	_pm.damping_max = 0.8
	_pm.lifetime_randomness = 0.35
	_pm.scale_min = 0.7
	_pm.scale_max = 1.3
	_pm.turbulence_enabled = true
	_pm.turbulence_noise_strength = 1.2
	_pm.turbulence_noise_scale = 2.5
	_pm.turbulence_noise_speed_random = 0.3
	_pm.turbulence_influence_min = 0.04
	_pm.turbulence_influence_max = 0.12

	# 出生时从 0 弹出，最后缩小消失
	var sc := Curve.new()
	sc.add_point(Vector2(0.0, 0.0))
	sc.add_point(Vector2(0.08, 1.0))
	sc.add_point(Vector2(0.75, 1.0))
	sc.add_point(Vector2(1.0, 0.0))
	var sct := CurveTexture.new()
	sct.curve = sc
	_pm.scale_curve = sct

	# 每片花瓣随机挑一个颜色（白 ~ 粉 ~ 深粉）
	var g := petal_colors
	if g == null:
		g = Gradient.new()
		g.set_color(0, Color(1.0, 0.86, 0.91))
		g.set_color(1, Color(0.93, 0.42, 0.62))
		g.add_point(0.5, Color(0.98, 0.64, 0.78))
	var gt := GradientTexture1D.new()
	gt.gradient = g
	_pm.color_initial_ramp = gt

	# 刚生成时偏亮（接住溶解边缘的光），随后回到本色
	var life := Gradient.new()
	life.set_color(0, Color(1.0, 1.0, 1.0))
	life.set_color(1, Color(0.85, 0.85, 0.85))
	var lt := GradientTexture1D.new()
	lt.gradient = life
	_pm.color_ramp = lt

	if petals_collide:
		_pm.collision_mode = ParticleProcessMaterial.COLLISION_RIGID
		_pm.collision_friction = 0.9
		_pm.collision_bounce = 0.0
		_pm.collision_use_scale = true

	_particles.process_material = _pm

	if detach_petals:
		var host: Node = get_tree().current_scene
		if host == null:
			host = get_tree().root
		host.add_child(_particles)
	else:
		add_child(_particles)
	_particles.global_transform = global_transform


func _build_emission_bands(world_aabb: AABB) -> void:
	_rng.randomize()
	var to_local: Transform3D = _particles.global_transform.affine_inverse()

	# 1) 收集所有三角形（世界坐标）及面积
	var tris := PackedVector3Array()
	var centers := PackedVector3Array()   # 每个三角形所属网格的中心，用于把法线朝外
	var cum := PackedFloat64Array()
	var total := 0.0
	for mi in _meshes:
		var mesh: Mesh = mi.mesh
		# 带骨骼的角色：用死亡瞬间的姿势采样，而不是 T-pose
		if not mi.skeleton.is_empty() and mi.get_node_or_null(mi.skeleton) is Skeleton3D:
			var baked: ArrayMesh = mi.bake_mesh_from_current_skeleton_pose()
			if baked != null:
				mesh = baked
		var faces: PackedVector3Array = mesh.get_faces()
		var xf: Transform3D = mi.global_transform
		var center: Vector3 = xf * mi.get_aabb().get_center()
		for i in range(0, faces.size(), 3):
			var a: Vector3 = xf * faces[i]
			var b: Vector3 = xf * faces[i + 1]
			var c: Vector3 = xf * faces[i + 2]
			var area := (b - a).cross(c - a).length() * 0.5
			if area <= 0.0:
				continue
			tris.append(a)
			tris.append(b)
			tris.append(c)
			centers.append(center)
			total += area
			cum.append(total)

	# 2) 按面积随机采样表面点，并算出每个点的溶解值 v
	var n := surface_samples
	var pos_l := PackedVector3Array()
	var nrm_l := PackedVector3Array()
	var vals := PackedFloat32Array()
	pos_l.resize(n)
	nrm_l.resize(n)
	vals.resize(n)
	var count := cum.size()
	for s in n:
		var r := _rng.randf() * total
		var ti := cum.bsearch(r)
		ti = mini(ti, count - 1)
		var a: Vector3 = tris[ti * 3]
		var b: Vector3 = tris[ti * 3 + 1]
		var c: Vector3 = tris[ti * 3 + 2]
		var u := _rng.randf()
		var w := _rng.randf()
		if u + w > 1.0:
			u = 1.0 - u
			w = 1.0 - w
		var p := a + (b - a) * u + (c - a) * w
		var nn := (b - a).cross(c - a).normalized()
		if nn.dot(p - centers[ti]) < 0.0:
			nn = -nn
		pos_l[s] = to_local * p
		nrm_l[s] = (to_local.basis * nn).normalized()
		vals[s] = _dissolve_value(p, world_aabb.position.y, world_aabb.end.y)

	# 3) 按 v 分桶，每个桶做一张「出生点贴图」
	_v_min = INF
	_v_max = -INF
	for v in vals:
		_v_min = minf(_v_min, v)
		_v_max = maxf(_v_max, v)
	_band_w = maxf((_v_max - _v_min) / float(bands), 0.0001)
	# 注意：Packed*Array 是值类型，取出来再 append 不会写回，所以这里用普通 Array
	var buckets: Array[Array] = []
	for i in bands:
		buckets.append([])
	for s in n:
		var bi := clampi(int((vals[s] - _v_min) / _band_w), 0, bands - 1)
		buckets[bi].append(s)

	_band_points.clear()
	_band_normals.clear()
	_band_counts = PackedInt32Array()
	_max_band_count = 1
	for i in bands:
		var idx: Array = buckets[i]
		var c := idx.size()
		_band_counts.append(c)
		_max_band_count = maxi(_max_band_count, c)
		if c == 0:
			_band_points.append(null)
			_band_normals.append(null)
			continue
		var pf := PackedFloat32Array()
		var nf := PackedFloat32Array()
		pf.resize(c * 3)
		nf.resize(c * 3)
		for j in c:
			var pp: Vector3 = pos_l[idx[j]]
			var np: Vector3 = nrm_l[idx[j]]
			pf[j * 3] = pp.x
			pf[j * 3 + 1] = pp.y
			pf[j * 3 + 2] = pp.z
			nf[j * 3] = np.x
			nf[j * 3 + 1] = np.y
			nf[j * 3 + 2] = np.z
		var pimg := Image.create_from_data(c, 1, false, Image.FORMAT_RGBF, pf.to_byte_array())
		var nimg := Image.create_from_data(c, 1, false, Image.FORMAT_RGBF, nf.to_byte_array())
		_band_points.append(ImageTexture.create_from_image(pimg))
		_band_normals.append(ImageTexture.create_from_image(nimg))


func _update_emission_band(p: float) -> void:
	var bi := int(floor((p - _v_min) / _band_w))
	if bi == _cur_band:
		return
	_cur_band = bi
	if bi < 0 or bi >= bands or _band_counts[bi] == 0:
		_particles.amount_ratio = 0.0
		return
	_pm.emission_point_texture = _band_points[bi]
	_pm.emission_normal_texture = _band_normals[bi]
	_pm.emission_point_count = _band_counts[bi]
	# 正在溶解的面积越大，花瓣越多
	_particles.amount_ratio = clampf(float(_band_counts[bi]) / float(_max_band_count), 0.12, 1.0)


# ------------------------------------------------------------------ 与 shader 同构的噪声

func _dissolve_value(p: Vector3, min_y: float, max_y: float) -> float:
	var h := clampf((p.y - min_y) / maxf(max_y - min_y, 0.0001), 0.0, 1.0)
	if top_down:
		h = 1.0 - h
	return lerpf(_fbm(p * noise_scale), h, height_weight)


static func _hash3(x: int, y: int, z: int) -> float:
	x += NOISE_OFFSET
	y += NOISE_OFFSET
	z += NOISE_OFFSET
	var h := ((x * 73856093) ^ (y * 19349663) ^ (z * 83492791)) & 0xFFFFFFFF
	h ^= h >> 16
	h = (h * 2146121005) & 0xFFFFFFFF
	h ^= h >> 15
	h = (h * 2221713035) & 0xFFFFFFFF
	h ^= h >> 16
	return float(h) / 4294967295.0


static func _value_noise(p: Vector3) -> float:
	var i := p.floor()
	var f := p - i
	var u := f * f * (Vector3(3, 3, 3) - 2.0 * f)
	var cx := int(i.x)
	var cy := int(i.y)
	var cz := int(i.z)
	var n000 := _hash3(cx, cy, cz)
	var n100 := _hash3(cx + 1, cy, cz)
	var n010 := _hash3(cx, cy + 1, cz)
	var n110 := _hash3(cx + 1, cy + 1, cz)
	var n001 := _hash3(cx, cy, cz + 1)
	var n101 := _hash3(cx + 1, cy, cz + 1)
	var n011 := _hash3(cx, cy + 1, cz + 1)
	var n111 := _hash3(cx + 1, cy + 1, cz + 1)
	var x00 := lerpf(n000, n100, u.x)
	var x10 := lerpf(n010, n110, u.x)
	var x01 := lerpf(n001, n101, u.x)
	var x11 := lerpf(n011, n111, u.x)
	return lerpf(lerpf(x00, x10, u.y), lerpf(x01, x11, u.y), u.z)


static func _fbm(p: Vector3) -> float:
	var n := 0.5 * _value_noise(p)
	n += 0.25 * _value_noise(p * 2.03 + Vector3(17.1, 17.1, 17.1))
	n += 0.125 * _value_noise(p * 4.01 + Vector3(31.7, 31.7, 31.7))
	return n / 0.875
