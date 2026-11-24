Shader "Custom/XRayMonitor"
{
    // Cath-lab monitor. The camera image holds how much X-ray each pixel absorbed (0 to 1).
    //  - Live fluoroscopy: transmitted light shown with a window, dense = dark, soft blur and grain.
    //  - Dye run (DSA): a mask frame taken before the dye is subtracted, leaving only the
    //    dye-filled vessels, dark on pale grey. Subtraction makes the image noisier.
    //  - Roadmap: the saved subtraction shown light over the live image.
    Properties
    {
        _MainTex ("Absorbed (live)", 2D) = "black" {}
        _Mask ("Absorbed mask (before dye)", 2D) = "black" {}
        _Roadmap ("Absorbed with dye (saved)", 2D) = "black" {}
        _Dsa ("Subtraction on", Range(0, 1)) = 0
        _HasMask ("Mask ready", Range(0, 1)) = 0
        _RoadmapOn ("Roadmap on", Range(0, 1)) = 0
        _DsaGain ("Subtraction strength", Range(0, 20)) = 5
        _RoadmapGain ("Roadmap strength", Range(0, 20)) = 5
        _WinLow ("Window low", Range(0, 1)) = 0.08
        _WinHigh ("Window high", Range(0, 1)) = 0.95
        _Gamma ("Gamma", Range(0.3, 2)) = 0.9
        _Grain ("Grain (live)", Range(0, 0.3)) = 0.05
        _GrainDsa ("Grain (subtraction)", Range(0, 0.3)) = 0.07
        _Blur ("Blur in texels", Range(0, 3)) = 1
        _Vignette ("Vignette", Range(0, 1)) = 0.45
        _Dim ("Dim (last image hold)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            sampler2D _MainTex, _Mask, _Roadmap;
            float4 _MainTex_TexelSize;
            half _Dsa, _HasMask, _RoadmapOn, _DsaGain, _RoadmapGain;
            half _WinLow, _WinHigh, _Gamma, _Grain, _GrainDsa, _Blur, _Vignette, _Dim;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            // Five-tap soft blur: real fluoroscopy is never razor sharp.
            float Soft(sampler2D tex, float2 uv)
            {
                float2 o = _MainTex_TexelSize.xy * _Blur;
                float s = tex2D(tex, uv).r * 0.4;
                s += tex2D(tex, uv + float2( o.x, 0)).r * 0.15;
                s += tex2D(tex, uv + float2(-o.x, 0)).r * 0.15;
                s += tex2D(tex, uv + float2(0,  o.y)).r * 0.15;
                s += tex2D(tex, uv + float2(0, -o.y)).r * 0.15;
                return s;
            }

            float Grain(float2 uv)
            {
                // Changes about 30 times a second, like quantum noise between frames.
                float2 p = floor(uv * _MainTex_TexelSize.zw) + floor(_Time.y * 30.0) * float2(17.0, 31.0);
                return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453) - 0.5;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float a = Soft(_MainTex, i.uv);
                float g;

                if (_Dsa > 0.5 && _HasMask > 0.5)
                {
                    // Subtraction: only what changed since the mask, which is the dye.
                    float m = Soft(_Mask, i.uv);
                    float d = saturate((a - m) * _DsaGain);
                    g = 0.88 - d * 0.78;
                    g += Grain(i.uv) * _GrainDsa;
                }
                else
                {
                    // Live: transmitted light through a window, dense tissue dark.
                    float t = 1.0 - a;
                    g = saturate((t - _WinLow) / max(1e-3, _WinHigh - _WinLow));
                    g = pow(g, _Gamma);
                    if (_RoadmapOn > 0.5 && _HasMask > 0.5)
                    {
                        float v = saturate((Soft(_Roadmap, i.uv) - Soft(_Mask, i.uv)) * _RoadmapGain);
                        g = lerp(g, 0.95, v * 0.65);
                    }
                    g += Grain(i.uv) * _Grain;
                }

                // Cool grey tint like a medical display, round field like an image intensifier.
                half3 col = saturate(g) * half3(0.93, 0.97, 1.0);
                float2 c = i.uv - 0.5;
                col *= 1.0 - _Vignette * saturate(dot(c, c) * 2.2);
                col *= 1.0 - _Dim * 0.35;
                // The grey levels above are display brightness. In a linear-colour project the
                // output gets brightened once more on the way to the screen, so undo that here.
                #ifndef UNITY_COLORSPACE_GAMMA
                col = GammaToLinearSpace(col);
                #endif
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
