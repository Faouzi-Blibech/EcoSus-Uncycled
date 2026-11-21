shader_type canvas_item;
// Colours the sprout's light body with the seat colour. Dark pixels (outline, eyes, the olive
// leaf) and green-leaning pixels (leaf highlights) keep their own colour.

uniform vec4 tint : source_color = vec4(1.0);

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	float bright = max(c.r, max(c.g, c.b));
	float keep = max(1.0 - clamp((bright - 0.62) / 0.16, 0.0, 1.0), clamp((c.g - c.r) / 0.05, 0.0, 1.0));
	c.rgb = mix(c.rgb * tint.rgb, c.rgb, keep);
	COLOR = c * COLOR;
}
