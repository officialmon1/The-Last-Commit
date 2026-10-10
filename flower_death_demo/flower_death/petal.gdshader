// 单片花瓣：在一个 QuadMesh 上用形状函数抠出带缺口的樱花花瓣，
// 顶点着色器让每片花瓣绕自己的随机轴翻滚、轻微卷曲。
// 颜色来自粒子系统的 COLOR（ParticleProcessMaterial 的颜色渐变）。
shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;

uniform float spin_speed = 2.2;         // 翻滚速度
uniform float flutter_speed = 7.0;      // 抖动频率
uniform float flutter_amount = 0.7;     // 抖动幅度（弧度）
uniform float curl = 0.35;              // 花瓣卷曲程度
uniform float notch_depth = 0.14;       // 樱花花瓣顶端缺口深度（0 = 无缺口）
uniform float emission_strength = 0.35; // 自发光，让花瓣在暗处也看得见
uniform float backlight_strength = 0.6; // 透光感

varying float v_seed;

float hash_u(uint x) {
	x ^= x >> 16u;
	x *= 2146121005u;
	x ^= x >> 15u;
	x *= 2221713035u;
	x ^= x >> 16u;
	return float(x) / 4294967295.0;
}

mat3 axis_angle(vec3 axis, float a) {
	float s = sin(a);
	float c = cos(a);
	float t = 1.0 - c;
	vec3 n = normalize(axis);
	return mat3(
		vec3(t * n.x * n.x + c,       t * n.x * n.y + s * n.z, t * n.x * n.z - s * n.y),
		vec3(t * n.x * n.y - s * n.z, t * n.y * n.y + c,       t * n.y * n.z + s * n.x),
		vec3(t * n.x * n.z + s * n.y, t * n.y * n.z - s * n.x, t * n.z * n.z + c));
}

void vertex() {
	uint id = uint(INSTANCE_ID) * 3u + 1u;
	float r0 = hash_u(id);
	float r1 = hash_u(id + 7919u);
	float r2 = hash_u(id + 15887u);
	v_seed = r0;

	// 卷曲：两侧向 +Z 翘起，像真花瓣一样不是平板
	VERTEX.z += curl * VERTEX.x * VERTEX.x * 8.0;

	vec3 axis = vec3(r0 - 0.5, r1 - 0.5, r2 - 0.5) + vec3(0.0, 0.0, 0.001);
	float spin = TIME * spin_speed * (0.5 + r1) + r2 * 6.2832;
	float flutter = sin(TIME * flutter_speed * (0.7 + 0.6 * r2) + r0 * 20.0) * flutter_amount;
	mat3 rot = axis_angle(axis, spin) * axis_angle(vec3(1.0, 0.0, 0.0), flutter);
	VERTEX = rot * VERTEX;
	NORMAL = rot * NORMAL;
}

void fragment() {
	// x: -1..1 横向，t: 0 = 花瓣根部（底），1 = 花瓣顶端
	float x = UV.x * 2.0 - 1.0;
	float t = 1.0 - UV.y;
	float half_width = 0.95 * sin(3.14159 * pow(clamp(t, 0.0, 1.0), 0.72));
	half_width *= mix(0.55, 1.0, smoothstep(0.0, 0.35, t)); // 根部更窄
	float ax = abs(x);
	if (ax > half_width) {
		discard;
	}
	// 顶端的 V 形缺口
	float notch_w = 0.32;
	if (ax < notch_w && t > 1.0 - notch_depth * (1.0 - ax / notch_w)) {
		discard;
	}
	vec3 c = COLOR.rgb;
	// 根部稍深、顶端稍浅，中间一条淡淡的脉络
	float shade = mix(0.78, 1.06, smoothstep(0.0, 0.8, t));
	float vein = 1.0 - 0.12 * (1.0 - smoothstep(0.0, 0.06, ax)) * (1.0 - t);
	vec3 col = c * shade * vein;
	ALBEDO = col;
	ROUGHNESS = 0.55;
	BACKLIGHT = col * backlight_strength;
	EMISSION = col * emission_strength;
}
