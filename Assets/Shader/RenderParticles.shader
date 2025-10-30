Shader "Custom/RenderParticles"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Blue ("Min Color", Color) = (0,0,1,1)
        _Red ("Max Color", Color) = (1,0,0,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" 
               "Queue" = "Transparent" 
               "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off 
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            // Blend One One  // additive 
            // Blend DstColor Zero //multiply

            HLSLPROGRAM

            #pragma vertex vert
            #pragma geometry geom 
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float3> positions; 
            StructuredBuffer<float3> velocities; 
            StructuredBuffer<float> densities; 
            cbuffer SimulationParams : register(b0)
            {
                float particleSize;
                float soundSpeed; 
            }
            float4 _BaseColor; 
            float4 _Blue; 
            float4 _Red; 

            struct Attributes
            {
                uint vid : SV_VertexID; 
            };

            struct v2g
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR; 
                float2 uv : TEXCOORD0; 
            };

            struct g2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR; 
                float2 uv : TEXCOORD0; 
            };

            v2g vert(Attributes IN)
            {
                v2g OUT;
                float vmax = soundSpeed; 
                float3 pos = positions[IN.vid];
                float vmag = length(velocities[IN.vid]);
                // float den = densities[IN.vid]; 

                OUT.pos = TransformObjectToHClip(float4(pos, 1));
                float normalized = saturate(vmag/vmax);
                // float normalized = saturate(den/10.0);
                OUT.color = lerp(_Blue, _Red, normalized);
                return OUT;
            }

            [maxvertexcount(6)]
            void geom(point v2g input[1], inout TriangleStream<g2f> triStream)
            {
                g2f o; 
                float size = particleSize*4;
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 offsets[4] = { float2(-size,-size*aspect), 
                                      float2(size,-size*aspect), 
                                      float2(size,size*aspect), 
                                      float2(-size,size*aspect) };

                float2 uvs[4] = { float2(0,0), float2(1,0), float2(1,1), float2(0,1) };

                int tri1[3] = {0,1,2};
                int tri2[3] = {0,2,3};

                for(int i=0;i<3;i++) 
                { 
                    o.pos = input[0].pos + float4(offsets[tri1[i]],0,0); 
                    o.color = input[0].color; 
                    o.uv = uvs[tri1[i]]; 
                    triStream.Append(o);
                }
                triStream.RestartStrip();
                for(int i=0;i<3;i++) 
                { 
                    o.pos = input[0].pos + float4(offsets[tri2[i]],0,0); 
                    o.color = input[0].color; 
                    o.uv = uvs[tri2[i]]; 
                    triStream.Append(o);
                }
                triStream.RestartStrip();
            }

            // Fragment Shader
            float4 frag(g2f IN) : SV_Target
            {
                float2 uv = (IN.uv-0.5)*2; 
                float r2 = dot(uv, uv);
                if (r2 > 1) discard; 
                float z = sqrt(1-r2);
                return float4(IN.color.rgb*z, 1);
                // return float4(IN.uv, 0, 0.5); 
            }
            ENDHLSL
        }
    }
}
