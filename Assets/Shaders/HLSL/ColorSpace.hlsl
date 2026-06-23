#ifndef STYLIZED_COLOR_SPACE_INCLUDED
#define STYLIZED_COLOR_SPACE_INCLUDED

float SafeCbrt(float value)
{
    return sign(value) * pow(abs(value), 1.0 / 3.0);
}

float3 RGBToOKLab(float3 rgb)
{
    float l =
        0.4122214708 * rgb.r +
        0.5363325363 * rgb.g +
        0.0514459929 * rgb.b;

    float m =
        0.2119034982 * rgb.r +
        0.6806995451 * rgb.g +
        0.1073969566 * rgb.b;

    float s =
        0.0883024619 * rgb.r +
        0.2817188376 * rgb.g +
        0.6299787005 * rgb.b;

    float3 lms = float3(
        SafeCbrt(l),
        SafeCbrt(m),
        SafeCbrt(s)
    );

    return float3(
        0.2104542553 * lms.x +
        0.7936177850 * lms.y -
        0.0040720468 * lms.z,

        1.9779984951 * lms.x -
        2.4285922050 * lms.y +
        0.4505937099 * lms.z,

        0.0259040371 * lms.x +
        0.7827717662 * lms.y -
        0.8086757660 * lms.z
    );
}

float3 OKLabToRGB(float3 lab)
{
    float l_ = lab.x + 0.3963377774 * lab.y + 0.2158037573 * lab.z;
    float m_ = lab.x - 0.1055613458 * lab.y - 0.0638541728 * lab.z;
    float s_ = lab.x - 0.0894841775 * lab.y - 1.2914855480 * lab.z;

    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;

    return float3(
         4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    );
}

float3 OKLabToOKLCH(float3 lab)
{
    return float3(
        lab.x,
        length(lab.yz),
        atan2(lab.z, lab.y)
    );
}

float3 OKLCHToOKLab(float3 lch)
{
    return float3(
        lch.x,
        cos(lch.z) * lch.y,
        sin(lch.z) * lch.y
    );
}

float3 LerpOKLab(float3 fromColor, float3 toColor, float t)
{
    float3 fromLab = RGBToOKLab(fromColor);
    float3 toLab = RGBToOKLab(toColor);
    return OKLabToRGB(lerp(fromLab, toLab, saturate(t)));
}

#endif
