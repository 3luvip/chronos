shader_type canvas_item;

uniform float shine_speed = 0.4;
uniform float shine_width = 0.15;
uniform float shine_angle = 0.3;
uniform float shine_intensity = 1.2;
uniform float pause_duration = 3.0;
uniform vec4 shine_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);

void fragment() {
    vec4 tex = texture(TEXTURE, UV);

    float cycle = 1.0 / shine_speed + pause_duration;
    float t = mod(TIME, cycle) * shine_speed;

    float skewed = UV.x + UV.y * tan(shine_angle);

    float streak = t * (1.0 + shine_width * 2.0) - shine_width;
    float dist = abs(skewed - streak);
    float glow = exp(-dist * dist / (shine_width * shine_width)) * step(t, 1.0);

    vec4 shine = shine_color * glow * shine_intensity * tex.a;
    COLOR = vec4(tex.rgb + shine.rgb, tex.a);
}

