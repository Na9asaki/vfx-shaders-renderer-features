Shader "Custom/VolumetricCube"
{
    Properties
    {
        _Color("Volume Color", Color) = (0.2, 0.6, 1, 1)
        _Density("Density", Range(0,5)) = 1
        _Steps("Raymarch Steps", Range(8,128)) = 32
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front // важно! мы смотрим внутрь куба

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes {
                float4 positionOS : POSITION;
            };
            struct Varyings {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float4 _Color;
            float _Density;
            int _Steps;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS).xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // простейший raymarch
            half4 frag(Varyings i) : SV_Target
            {
                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(i.positionWS - rayOrigin);

                // найдем пересечение с кубом [-0.5, 0.5] (локальный space)
                float3 ro = TransformWorldToObject(rayOrigin).xyz;
                float3 rd = normalize(mul((float3x3)UNITY_MATRIX_I_M, rayDir));

                // пересечение со "сферой" для простоты
                float t = 0;
                float stepSize = 1.0 / _Steps;
                float3 accumColor = 0;
                float accumAlpha = 0;

                [loop]
                for (int s=0; s<_Steps; s++)
                {
                    float3 p = ro + rd * t;
                    // внутри куба?
                    if (all(abs(p) < 0.5))
                    {
                        float density = exp(-t * _Density);
                        float3 col = _Color.rgb * density;
                        float alpha = 0.05;

                        accumColor += (1 - accumAlpha) * col * alpha;
                        accumAlpha += (1 - accumAlpha) * alpha;
                    }
                    t += stepSize;
                }

                return float4(accumColor, accumAlpha);
            }
            ENDHLSL
        }
    }
}
