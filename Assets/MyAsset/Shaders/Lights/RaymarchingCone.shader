Shader "Custom/VolumetricCone"
{
    Properties
    {
        _Color("Volume Color", Color) = (0.2, 0.6, 1, 1)
        _Density("Density", Range(0,5)) = 5
        _Steps("Raymarch Steps", Range(8,128)) = 64
        _Height("Cone Height", Range(0.1,5)) = 1
        _Radius("Cone Base Radius", Range(0.1,2)) = 0.5
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front

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
            float _Height;
            float _Radius;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS).xyz;
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // SDF конуса с верхом в y=0 и основанием вниз
            float coneSDF(float3 p)
            {
                // смещаем точку так, чтобы верх был в y=0, основание в y=-_Height
                float y = -p.y; 
                if (y < 0 || y > _Height) return 1e5; // вне конуса по y
                float k = _Radius * (1.0 - y/_Height);
                return length(p.xz) - k; // расстояние до поверхности конуса
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(i.positionWS - rayOrigin);

                // в локальные координаты конуса
                float3 ro = TransformWorldToObject(rayOrigin).xyz;
                float3 rd = normalize(mul((float3x3)UNITY_MATRIX_I_M, rayDir));

                float t = 0;
                float stepSize = 1.0 / _Steps;
                float3 accumColor = 0;
                float accumAlpha = 0;

                [loop]
                for (int s=0; s<_Steps; s++)
                {
                    float3 p = ro + rd * t;
                    float d = coneSDF(p);

                    // чем ближе к поверхности, тем больше плотность
                    float alpha = exp(-d * _Density) * 0.05;

                    accumColor += (1 - accumAlpha) * _Color.rgb * alpha;
                    accumAlpha += (1 - accumAlpha) * alpha;

                    t += stepSize;
                }

                return float4(accumColor, accumAlpha);
            }
            ENDHLSL
        }
    }
}
