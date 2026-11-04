#version 330 core
in vec3 normal;
uniform vec4 uColor;
uniform bool uUnlit;
uniform vec3 uLightDirections[3];
uniform vec3 uLightColors[3];
out vec4 fragmentColor;

void main()
{
    vec3 n = normalize(normal);
    if (!gl_FrontFacing) n = -n;
    vec3 illumination = vec3(0.04);
    for (int i = 0; i < 3; ++i)
        illumination += uLightColors[i] * max(dot(n, -uLightDirections[i]), 0.0);
    vec3 linearColor = uColor.rgb * (uUnlit ? vec3(1.0) : illumination);
    fragmentColor = vec4(pow(linearColor, vec3(1.0 / 2.2)), 1.0);
}
