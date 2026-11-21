shader_type canvas_item;
// Soft drop shadow from a texture's alpha. Drawn on a rect larger than the art by `pad` (in UV)
// on each side so the blur can spill past the art's edges. `blur` is the radius in art UV.

uniform vec2 pad = vec2(0.0);
uniform vec2 blur = vec2(0.01);
uniform vec4 shadow_color : source_color = vec4(0.0, 0.0, 0.0, 0.3);

float alpha_at(sampler2D tex, vec2 uv) {
	if (uv.x < 0.0 || uv.y < 0.0 || uv.x > 1.0 || uv.y > 1.0) {
		return 0.0;
	}
	return texture(tex, uv).a;
}

void fragment() {
	vec2 inner = (UV - pad) / (vec2(1.0) - 2.0 * pad);
	float sum = 0.0;
	float weight = 0.0;
	for (int x = -2; x <= 2; x++) {
		for (int y = -2; y <= 2; y++) {
			float w = 1.0 / (1.0 + float(x * x + y * y));
			sum += alpha_at(TEXTURE, inner + vec2(float(x), float(y)) * blur * 0.5) * w;
			weight += w;
		}
	}
	COLOR = vec4(shadow_color.rgb, shadow_color.a * sum / weight);
}
