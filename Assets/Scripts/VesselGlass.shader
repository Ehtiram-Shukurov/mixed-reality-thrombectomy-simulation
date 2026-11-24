Shader "Custom/VesselGlass"
{
    // Smooth vessel surface. Faces turned toward you are lit and see-through,
    // edges are bright and solid, so the wire inside stays visible.
    // Per-vertex distances along the vessel tree (uv2) drive two effects:
    //   x = distance from the heart end -> moving blood-flow streaks, dim where no blood reaches
    //   y = distance from the dye injection point -> a dye front that spreads and stops at the clot
    Properties
    {
        _Color ("Vessel Color", Color) = (0.74, 0.16, 0.18, 1)
        _RimColor ("Edge Color", Color) = (1.0, 0.62, 0.56, 1)
        _CenterAlpha ("Opacity facing you", Range(0, 1)) = 0.38
        _EdgeAlpha ("Opacity at edges", Range(0, 1)) = 0.95
        _RimPower ("Edge sharpness", Range(0.5, 8)) = 2.2

        [Header(Blood flow)]
        _FlowOn ("Flow on", Range(0, 1)) = 1
        _FlowStrength ("Streak brightness", Range(0, 1)) = 0.22
        _FlowFreq ("Streaks per volume size", Float) = 14
        _FlowSpeed ("Streak speed", Float) = 0.9
        _NoFlowDim ("Dimming where no blood reaches", Range(0, 1)) = 0.45

        [Header(Dye)]
        _DyeColor ("Dye Color", Color) = (0.85, 0.97, 1.0, 1)
        _DyeFront ("Dye front distance", Float) = 0
        _DyeTail ("Dye washed out up to", Float) = 0
        _DyeAlpha ("Dye amount", Range(0, 1)) = 0
        _DyeFeather ("Dye front softness", Float) = 0.04
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

            half4 _Color, _RimColor, _DyeColor;
            half _CenterAlpha, _EdgeAlpha, _RimPower;
            half _FlowOn, _FlowStrength, _FlowFreq, _FlowSpeed, _NoFlowDim;
            float _DyeFront, _DyeTail, _DyeAlpha, _DyeFeather;

            // Matches VesselMeshView.Unreached.
            #define UNREACHED 9000.0

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 dist : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                float2 dist : TEXCOORD2;
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
                o.dist = v.dist;
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

                // Blood flow. Before the graph is mapped every vertex is "unreached", so this
                // stays off and the vessels look as before.
                bool mapped = i.dist.x < UNREACHED || i.dist.y < UNREACHED;
                if (_FlowOn > 0.5 && mapped)
                {
                    if (i.dist.x < UNREACHED)
                    {
                        float s = frac(i.dist.x * _FlowFreq - _Time.y * _FlowSpeed);
                        half streak = smoothstep(0.0, 0.15, s) * (1.0 - smoothstep(0.15, 0.45, s));
                        col += _RimColor.rgb * streak * _FlowStrength;
                    }
                    else
                    {
                        // Past the clot: no blood, so the vessel looks dim and grey.
                        half g = dot(col, half3(0.3, 0.59, 0.11));
                        col = lerp(col, g.xxx * 0.8, _NoFlowDim);
                    }
                }

                // Dye front spreading along the vessels from the injection point.
                if (_DyeAlpha > 0.001 && i.dist.y < UNREACHED)
                {
                    half dye = saturate((_DyeFront - i.dist.y) / max(_DyeFeather, 1e-4)) * _DyeAlpha;
                    // Washout: vessels behind the tail have already cleared.
                    dye *= saturate((i.dist.y - _DyeTail) / max(_DyeFeather, 1e-4) + (_DyeTail <= 0 ? 1 : 0));
                    // A brighter band right at the front makes the movement easy to follow.
                    half edge = saturate(1.0 - abs(_DyeFront - i.dist.y) / max(_DyeFeather * 2.0, 1e-4)) * _DyeAlpha;
                    col = lerp(col, _DyeColor.rgb, dye * 0.85) + _DyeColor.rgb * edge * 0.35;
                    a = max(a, dye * 0.9);
                }

                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
