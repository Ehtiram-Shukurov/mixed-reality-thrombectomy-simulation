Shader "Custom/XRayMonitor"
{
    // Shows the monitor camera's image like a cath-lab screen: greyscale, inverted so dye
    // reads dark on a light background, with a soft vignette.
    Properties
    {
        _MainTex ("Image", 2D) = "black" {}
        _Invert ("Invert (dark dye on light)", Range(0, 1)) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1
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

            sampler2D _MainTex;
            half _Invert, _Brightness, _Vignette, _Dim;

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

            fixed4 frag (v2f i) : SV_Target
            {
                fixed3 c = tex2D(_MainTex, i.uv).rgb;
                half g = dot(c, half3(0.299, 0.587, 0.114)) * _Brightness;
                g = lerp(g, 1.0 - g, _Invert);
                // Cool grey tint like a medical display.
                half3 col = g * half3(0.93, 0.97, 1.0);
                float2 d = i.uv - 0.5;
                col *= 1.0 - _Vignette * saturate(dot(d, d) * 2.2);
                col *= 1.0 - _Dim * 0.35;
                return fixed4(col, 1);
            }
            ENDCG
        }
    }
}
