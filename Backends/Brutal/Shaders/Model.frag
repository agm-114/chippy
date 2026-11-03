#version 330 core
in vec3 normal;
uniform vec4 uColor;
uniform bool uUnlit;
out vec4 fragmentColor;

void main()
{
    vec3 n = normalize(normal);
    if (!gl_FrontFacing) n = -n;
    float diffuse = max(dot(n, normalize(vec3(-0.5, 0.8, 0.6))), 0.0);
    vec3 linearColor = uColor.rgb * (uUnlit ? 1.0 : 0.28 + 0.72 * diffuse);
    fragmentColor = vec4(pow(linearColor, vec3(1.0 / 2.2)), 1.0);
}
