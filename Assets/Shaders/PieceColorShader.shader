Shader "BizarreChess/PieceColor"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PrimaryColor ("Primary Color", Color) = (0.95, 0.92, 0.85, 1)
        _SecondaryColor ("Secondary Color", Color) = (0.85, 0.82, 0.75, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.7
        _Metallic ("Metallic", Range(0, 1)) = 0.0
        _ColorThreshold ("Color Match Threshold", Range(0, 0.5)) = 0.2
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 vertexColor : COLOR;
                float fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _PrimaryColor;
                float4 _SecondaryColor;
                float _Smoothness;
                float _Metallic;
                float _ColorThreshold;
            CBUFFER_END

            // Check if a color matches green (0, 1, 0) within threshold
            bool IsGreen(float3 color)
            {
                return color.g > (1.0 - _ColorThreshold) && 
                       color.r < _ColorThreshold && 
                       color.b < _ColorThreshold;
            }

            // Check if a color matches magenta (1, 0, 1) within threshold
            bool IsMagenta(float3 color)
            {
                return color.r > (1.0 - _ColorThreshold) && 
                       color.b > (1.0 - _ColorThreshold) && 
                       color.g < _ColorThreshold;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);
                
                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.vertexColor = input.color;
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // Sample texture
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Also consider vertex color (mesh stores original colors)
                float4 baseColor = input.vertexColor;
                
                // Use texture if available, otherwise use vertex color
                if (texColor.a > 0.01)
                {
                    baseColor = texColor;
                }
                
                // Replace green with primary player color
                float3 finalColor = baseColor.rgb;
                if (IsGreen(baseColor.rgb))
                {
                    finalColor = _PrimaryColor.rgb;
                }
                // Replace magenta with secondary player color
                else if (IsMagenta(baseColor.rgb))
                {
                    finalColor = _SecondaryColor.rgb;
                }
                // Otherwise keep original color (static accents)
                
                // Basic lighting
                Light mainLight = GetMainLight();
                float3 normalWS = normalize(input.normalWS);
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float3 diffuse = finalColor * mainLight.color * NdotL;
                float3 ambient = finalColor * 0.3; // Simple ambient
                
                // Specular
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfDir = normalize(mainLight.direction + viewDir);
                float spec = pow(saturate(dot(normalWS, halfDir)), 32 * _Smoothness + 1) * _Smoothness;
                float3 specular = mainLight.color * spec * _Metallic;
                
                float3 finalLit = ambient + diffuse + specular;
                
                // Apply fog
                finalLit = MixFog(finalLit, input.fogFactor);
                
                return half4(finalLit, baseColor.a);
            }
            ENDHLSL
        }
        
        // Shadow caster pass
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            float3 _LightDirection;

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    
    // Fallback for non-URP
    Fallback "Standard"
}

