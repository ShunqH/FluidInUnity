Shader "Custom/BoxOutLine"
{
    Properties
    {
        _Color("Base Color", Color) = (0.1, 0.1, 1, 0.2)
        _EdgeColor ("Edge Color", Color) = (1, 1, 1, 1)
        _EdgeThickness ("Edge Thickness", Range(0,5)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" 
               "Queue" = "Transparent"
               "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL; 
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : NORMAL; 
            };

            float4 _Color;
            float4 _EdgeColor;
            float _EdgeThickness; 

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.vertex.xyz);
                OUT.worldPos = mul(unity_ObjectToWorld, IN.vertex).xyz;
                OUT.normal = TransformObjectToWorldNormal(IN.normal);
                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 viewDir = normalize(_WorldSpaceCameraPos - IN.worldPos); 
                float edge = 1 - abs(dot(IN.normal, viewDir));
                float intensity = pow(edge, _EdgeThickness); 
                return lerp(_Color, _EdgeColor, intensity);
            }
            ENDHLSL
        }
    }
}
