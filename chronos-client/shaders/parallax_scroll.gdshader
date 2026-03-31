shader_type canvas_item;

uniform vec2 direction = vec2(1.0, 0.0);
uniform float speed_scale = 0.02;
uniform float time_offset = 0.0;

void fragment() {
    vec2 move = direction * (TIME - time_offset) * speed_scale;
    COLOR = texture(TEXTURE, fract(UV + move));
}

