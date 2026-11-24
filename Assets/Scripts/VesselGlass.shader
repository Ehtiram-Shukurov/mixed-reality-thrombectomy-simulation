Shader "Custom/VesselGlass"
{
    // Smooth vessel surface. Faces turned toward you are lit and see-through,
    // edges are bright and solid, so the wire inside stays visible.
    Properties
    {
        _Color ("Vessel Color", Color) = (0.74, 0.16, 0.18, 1)
        _RimColor ("Edge Color", Color) = (1.0, 0.62, 0.56, 1)
        _CenterAlpha ("Opacity facing you", Range(0, 1)) = 0.38
        _EdgeAlpha ("Opacity at edges", Range(0, 1)) = 0.95
        _RimPower ("Edge sharpness", Range(0.5, 8)) = 2.2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            half4 _Color, _RimColor;
            half _CenterAlpha, _EdgeAlpha, _RimPower;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 n = normalize(i.worldNormal);
                float3 v = normalize(UnityWorldSpaceViewDir(i.worldPos));
                // Mesh winding is not guaranteed, so treat both sides the same.
                half ndv = abs(dot(n, v));
                half rim = pow(1.0 - ndv, _RimPower);

                half3 col = _Color.rgb * (0.40 + 0.60 * ndv) + _RimColor.rgb * rim * 0.55;
                half a = lerp(_CenterAlpha, _EdgeAlpha, rim);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
