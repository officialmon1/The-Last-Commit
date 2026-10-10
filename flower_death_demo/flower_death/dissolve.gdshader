// 角色身体的溶解 shader：按「高度 + 3D 噪声」逐片消失，边缘发出花色光。
// progress 由 flower_dissolve.gd 每帧推进。噪声函数与 GDScript 端完全同构，
// 这样 CPU 端能算出每个表面点何时消失，花瓣才能正好从溶解边缘飘出来。
shader_type spatial;
render_mode cull_back, depth_draw_opaque;

// ---- 原材质（从角色原来的 StandardMaterial3D 拷贝过来） ----
uniform vec4 albedo : source_color = vec4(0.85, 0.82, 0.78, 1.0);
uniform sampler2D albedo_texture : source_color, filter_linear_mipmap, hint_default_white;
uniform float roughness : hint_range(0.0, 1.0) = 0.7;
uniform float metallic : hint_range(0.0, 1.0) = 0.0;

// ---- 溶解参数 ----
uniform float progress = -1.0;          // 溶解阈值：值小于它的部分被丢弃
uniform float min_y = 0.0;              // 角色在世界空间的最低点
uniform float max_y = 2.0;              // 角色在世界空间的最高点
uniform bool top_down = true;           // true：从头往脚消散
uniform float height_weight : hint_range(0.0, 1.0) = 0.65;
uniform float noise_scale = 5.0;
uniform float edge_width : hint_range(0.0, 0.3) = 0.05;
uniform vec4 edge_color : source_color = vec4(1.0, 0.58, 0.78, 1.0);
uniform float edge_emission = 5.0;

varying vec3 world_pos;

// 32 位整数哈希（lowbias32 变体）。必须与 flower_dissolve.gd 中的 _hash3 保持一致。
float hash3(ivec3 c) {
	uvec3 u = uvec3(c + ivec3(4096));
	uint h = (u.x * 73856093u) ^ (u.y * 19349663u) ^ (u.z * 83492791u);
	h ^= h >> 16u;
	h *= 2146121005u;
	h ^= h >> 15u;
	h *= 2221713035u;
	h ^= h >> 16u;
	return float(h) / 4294967295.0;
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = p - i;
	vec3 u = f * f * (3.0 - 2.0 * f);
	ivec3 c = ivec3(i);
	float n000 = hash3(c);
	float n100 = hash3(c + ivec3(1, 0, 0));
	float n010 = hash3(c + ivec3(0, 1, 0));
	float n110 = hash3(c + ivec3(1, 1, 0));
	float n001 = hash3(c + ivec3(0, 0, 1));
	float n101 = hash3(c + ivec3(1, 0, 1));
	float n011 = hash3(c + ivec3(0, 1, 1));
	float n111 = hash3(c + ivec3(1, 1, 1));
	float x00 = mix(n000, n100, u.x);
	float x10 = mix(n010, n110, u.x);
	float x01 = mix(n001, n101, u.x);
	float x11 = mix(n011, n111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

float fbm(vec3 p) {
	float n = 0.5 * value_noise(p);
	n += 0.25 * value_noise(p * 2.03 + vec3(17.1));
	n += 0.125 * value_noise(p * 4.01 + vec3(31.7));
	return n / 0.875;
}

float dissolve_value(vec3 p) {
	float h = clamp((p.y - min_y) / max(max_y - min_y, 0.0001), 0.0, 1.0);
	if (top_down) {
		h = 1.0 - h;
	}
	return mix(fbm(p * noise_scale), h, height_weight);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float d = dissolve_value(world_pos) - progress;
	if (d < 0.0) {
		discard;
	}
	vec3 base = albedo.rgb * texture(albedo_texture, UV).rgb;
	float edge = 1.0 - smoothstep(0.0, edge_width, d);
	ALBEDO = mix(base, edge_color.rgb, edge);
	ROUGHNESS = roughness;
	METALLIC = metallic;
	EMISSION = edge_color.rgb * edge * edge_emission;
}
