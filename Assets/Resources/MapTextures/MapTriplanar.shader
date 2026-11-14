// Lit URP shader that lays a texture onto a surface by its world position (triplanar),
// so it works on the map's meshes even though their UVs are missing or messy.
// Walls get the texture from the side, floors and ceilings from above, blended at the edges.
Shader "Blackout/MapTriplanar"
{
    Properties
    {
        _BaseMap ("Color", 2D) = "white" {}
        [Normal] _NormalMap ("Normal", 2D) = "bump" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _TileSize ("Tile size (metres per repeat)", Float) = 2
        _NormalStrength ("Normal strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
        _Metallic ("Metallic", Range(0, 1)) = 0
        // Structure mode: one material for a whole room mesh. Sides use the main texture,
        // upward faces the floor texture and downward faces the ceiling texture.
        [Toggle] _Structure ("Structure mode", Float) = 0
        _FloorMap ("Floor color", 2D) = "white" {}
        [Normal] _FloorNormal ("Floor normal", 2D) = "bump" {}
        _FloorTile ("Floor tile size", Float) = 1.6
        _CeilMap ("Ceiling color", 2D) = "white" {}
        [Normal] _CeilNormal ("Ceiling normal", 2D) = "bump" {}
        _CeilTile ("Ceiling tile size", Float) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);   SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
            TEXTURE2D(_FloorMap);  TEXTURE2D(_FloorNormal);
            TEXTURE2D(_CeilMap);   TEXTURE2D(_CeilNormal);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _NormalMap_ST;
                half4 _BaseColor;
                float _TileSize;
                half _NormalStrength;
                half _Smoothness;
                half _Metallic;
                float _Structure;
                float4 _FloorMap_ST;
                float4 _FloorNormal_ST;
                float _FloorTile;
                float4 _CeilMap_ST;
                float4 _CeilNormal_ST;
                float _CeilTile;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalWS);
                float3 p = input.positionWS / max(_TileSize, 0.01);

                // How much each projection counts: mostly the one facing the surface
                float3 w = pow(abs(n), 4.0);
                w /= max(w.x + w.y + w.z, 1e-4);

                float2 uvX = p.zy;
                float2 uvY = p.xz;
                float2 uvZ = p.xy;

                // Top/bottom projection: the main texture, or floor/ceiling textures in structure mode
                half3 colY;
                half3 tnY;
                UNITY_BRANCH
                if (_Structure > 0.5)
                {
                    float2 uvF = input.positionWS.xz / max(_FloorTile, 0.01);
                    float2 uvC = input.positionWS.xz / max(_CeilTile, 0.01);
                    half up = step(0.0, n.y);
                    colY = lerp(SAMPLE_TEXTURE2D(_CeilMap, sampler_BaseMap, uvC).rgb,
                                SAMPLE_TEXTURE2D(_FloorMap, sampler_BaseMap, uvF).rgb, up);
                    tnY = lerp(UnpackNormalScale(SAMPLE_TEXTURE2D(_CeilNormal, sampler_NormalMap, uvC), _NormalStrength),
                               UnpackNormalScale(SAMPLE_TEXTURE2D(_FloorNormal, sampler_NormalMap, uvF), _NormalStrength), up);
                }
                else
                {
                    colY = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * _BaseColor.rgb;
                    tnY = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvY), _NormalStrength);
                }

                half3 albedo =
                      SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * _BaseColor.rgb * w.x
                    + colY * w.y
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * _BaseColor.rgb * w.z;

                // Triplanar normal map (whiteout blend)
                half3 tnX = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvX), _NormalStrength);
                half3 tnZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uvZ), _NormalStrength);
                tnX = half3(tnX.xy + n.zy, abs(tnX.z) * n.x);
                tnY = half3(tnY.xy + n.xz, abs(tnY.z) * n.y);
                tnZ = half3(tnZ.xy + n.xy, abs(tnZ.z) * n.z);
                float3 normalWS = normalize(tnX.zyx * w.x + tnY.xzy * w.y + tnZ.xyz * w.z);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogFactor;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;

                half4 color = UniversalFragmentPBR(inputData, surface);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }

        // Shadows, depth and normals (used by ambient occlusion) come from URP's own Lit shader
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }

    FallBack "Universal Render Pipeline/Lit"
}
