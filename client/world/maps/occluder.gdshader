shader_type canvas_item;
// A depth piece cut from the room art as a soft oval (tree tops, bushes), so no square of the
// ground around it is drawn over a player standing behind it. `region` is the piece's rect in
// texture UV (x, y, w, h): a Sprite2D region's UV covers the whole texture.

uniform vec4 region = vec4(0.0, 0.0, 1.0, 1.0);
uniform float feather = 0.12;

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	vec2 p = (UV - region.xy) / region.zw * 2.0 - vec2(1.0);
	float d = length(p);
	c.a *= 1.0 - smoothstep(1.0 - feather, 1.0, d);
	COLOR = c * COLOR;
}
