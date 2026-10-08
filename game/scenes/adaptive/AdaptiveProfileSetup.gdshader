shader_type canvas_item;

uniform float radius : hint_range(0.0, 0.5) = 0.12;

void fragment() {
    vec4 color = texture(TEXTURE, UV);

    vec2 p = UV - vec2(0.5);
    vec2 half_size = vec2(0.5 - radius);

    vec2 q = abs(p) - half_size;

    float dist = length(max(q, vec2(0.0)))
               + min(max(q.x, q.y), 0.0)
               - radius;

    float alpha = 1.0 - smoothstep(-0.005, 0.005, dist);

    COLOR = vec4(color.rgb, color.a * alpha);
}