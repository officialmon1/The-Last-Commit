extends Node3D
## 演示场景：一个用基本几何体拼的小角色。
## 空格 = 死亡（花瓣消散）  R = 复活  T = 切换「从上往下 / 从下往上」

var character: Node3D
var dissolve: FlowerDissolve
var label: Label
var _idle_t := 0.0


func _ready() -> void:
	_build_environment()
	_build_floor()
	character = _build_character()
	add_child(character)

	dissolve = FlowerDissolve.new()
	dissolve.name = "FlowerDissolve"
	character.add_child(dissolve)
	dissolve.dissolve_finished.connect(func(): _set_status("已消散。按 R 复活"))

	_build_ui()


func _process(delta: float) -> void:
	# 活着的时候轻轻呼吸摆动
	if not dissolve.is_playing and character.visible:
		_idle_t += delta
		character.rotation.y = sin(_idle_t * 0.6) * 0.35


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		match event.keycode:
			KEY_SPACE:
				if not dissolve.is_playing:
					dissolve.play()
					_set_status("消散中…")
			KEY_R:
				dissolve.reset()
				_set_status("空格：死亡   R：复活   T：切换方向")
			KEY_T:
				dissolve.top_down = not dissolve.top_down
				_set_status("方向：" + ("从上往下" if dissolve.top_down else "从下往上"))


# ------------------------------------------------------------------ 场景搭建

func _build_environment() -> void:
	var sky_mat := ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(0.18, 0.2, 0.36)
	sky_mat.sky_horizon_color = Color(0.62, 0.5, 0.6)
	sky_mat.ground_horizon_color = Color(0.4, 0.33, 0.4)
	sky_mat.ground_bottom_color = Color(0.1, 0.1, 0.14)
	var sky := Sky.new()
	sky.sky_material = sky_mat

	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.ambient_light_energy = 0.8
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.glow_enabled = true
	env.glow_intensity = 0.9
	env.glow_bloom = 0.05
	var we := WorldEnvironment.new()
	we.environment = env
	add_child(we)

	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-40, 35, 0)
	sun.light_color = Color(1.0, 0.92, 0.85)
	sun.light_energy = 1.1
	sun.shadow_enabled = true
	add_child(sun)

	var cam := Camera3D.new()
	cam.position = Vector3(0.2, 1.25, 3.0)
	cam.fov = 55.0
	add_child(cam)
	cam.look_at(Vector3(0.45, 0.9, 0))


func _build_floor() -> void:
	var floor_mi := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(30, 30)
	var m := StandardMaterial3D.new()
	m.albedo_color = Color(0.22, 0.26, 0.24)
	m.roughness = 0.95
	plane.material = m
	floor_mi.mesh = plane
	add_child(floor_mi)

	# 让花瓣能落在地上
	var col := GPUParticlesCollisionBox3D.new()
	col.size = Vector3(30, 1, 30)
	col.position = Vector3(0, -0.5, 0)
	add_child(col)


func _part(mesh: Mesh, mat: Material, pos: Vector3, rot_deg := Vector3.ZERO) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.mesh = mesh
	mi.material_override = mat
	mi.position = pos
	mi.rotation_degrees = rot_deg
	return mi


func _build_character() -> Node3D:
	var root := Node3D.new()
	root.name = "Character"

	var cloth := StandardMaterial3D.new()
	cloth.albedo_color = Color(0.32, 0.42, 0.72)
	cloth.roughness = 0.8
	var skin := StandardMaterial3D.new()
	skin.albedo_color = Color(0.95, 0.8, 0.7)
	skin.roughness = 0.6
	var hair := StandardMaterial3D.new()
	hair.albedo_color = Color(0.2, 0.14, 0.12)
	hair.roughness = 0.7
	var dark := StandardMaterial3D.new()
	dark.albedo_color = Color(0.16, 0.16, 0.2)

	var leg := CapsuleMesh.new()
	leg.radius = 0.09
	leg.height = 0.8
	root.add_child(_part(leg, dark, Vector3(-0.12, 0.4, 0)))
	root.add_child(_part(leg, dark, Vector3(0.12, 0.4, 0)))

	var body := CapsuleMesh.new()
	body.radius = 0.22
	body.height = 0.75
	root.add_child(_part(body, cloth, Vector3(0, 1.05, 0)))

	var arm := CapsuleMesh.new()
	arm.radius = 0.07
	arm.height = 0.62
	root.add_child(_part(arm, cloth, Vector3(-0.3, 1.05, 0), Vector3(0, 0, -12)))
	root.add_child(_part(arm, cloth, Vector3(0.3, 1.05, 0), Vector3(0, 0, 12)))

	var head := SphereMesh.new()
	head.radius = 0.17
	head.height = 0.34
	root.add_child(_part(head, skin, Vector3(0, 1.58, 0)))

	var hair_mesh := SphereMesh.new()
	hair_mesh.radius = 0.18
	hair_mesh.height = 0.26
	hair_mesh.is_hemisphere = true
	root.add_child(_part(hair_mesh, hair, Vector3(0, 1.62, -0.01)))
	return root


func _build_ui() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	label = Label.new()
	label.position = Vector2(16, 12)
	label.add_theme_font_size_override("font_size", 20)
	label.add_theme_color_override("font_color", Color(1, 1, 1, 0.9))
	layer.add_child(label)
	_set_status("空格：死亡   R：复活   T：切换方向")


func _set_status(t: String) -> void:
	label.text = t
