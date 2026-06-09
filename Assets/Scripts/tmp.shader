Shader "Custom/DICOM_Volume_MIP_VR_Quest3"
{
    Properties
    {
        [Header(Volume Data)]
        _VolumeTex ("Volume Texture (3D)", 3D) = "" {}
        _GridSizeX ("Grid Size X", Float) = 512
        _GridSizeY ("Grid Size Y", Float) = 512
        _GridSizeZ ("Grid Size Z", Float) = 256

        [Header(Raymarching Settings)]
        _Steps ("Raymarch Steps", Range(16, 128)) = 64
        _Intensity ("Intensity Multiplier", Range(0.01, 2)) = 0.3
        _Threshold ("Density Threshold", Range(0.0, 1.0)) = 0.05
        
        [Header(VR Hand Interaction)]
        _LeftHandPos ("Left Hand Position (UV Space)", Vector) = (0,0,0,0)
        _RightHandPos ("Right Hand Position (UV Space)", Vector) = (0,0,0,0)
        _ClipRadius ("Hand Clip Radius", Range(0.0, 0.5)) = 0.1

        [Header(Colors)]
        _mriBlack ("MRI Black Level", Color) = (0.01, 0.02, 0.04)
        _mriSteel ("MRI Steel Level", Color) = (0.45, 0.52, 0.6)
        _mriWhite ("MRI White Level", Color) = (1.0, 1.0, 1.0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Front

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // ES3.1 target unlocks full half-precision ALU on Adreno 740
            #pragma target 3.5

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos         : SV_POSITION;
                // float3 not half3 -- half interpolants cause stereo ghosting on Adreno
                float3 localPos    : TEXCOORD0;
                float3 localCamPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler3D _VolumeTex;
            float _GridSizeX, _GridSizeY, _GridSizeZ;
            int   _Steps;
            half  _Intensity;
            half  _Threshold;

            float4 _LeftHandPos;
            float4 _RightHandPos;
            half   _ClipRadius;

            half3 _mriBlack;
            half3 _mriSteel;
            half3 _mriWhite;

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos      = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;

                #if defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
                    float3 camPos = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #else
                    float3 camPos = _WorldSpaceCameraPos;
                #endif

                // Camera->object-space done in vertex shader: saves a matrix multiply per fragment
                o.localCamPos = mul(unity_WorldToObject, float4(camPos, 1.0)).xyz;

                return o;
            }

            half4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // -- 1. RAY SETUP (identical to original -- proven correct) ----
                float3 ro = i.localCamPos;

                // Original guard preserved exactly -- the clip() replacement was
                // incorrectly discarding valid pixels from certain camera angles
                float3 rawRayDir = i.localPos - ro;
                if (length(rawRayDir) < 1e-6) {
                    rawRayDir = float3(1e-6, 1e-6, 1e-6);
                }

                float3 rayDir = normalize(rawRayDir);

                // Safe reciprocal (original logic, just using rcp() instead of 1.0/x)
                float3 sd = rayDir;
                sd.x = (abs(sd.x) < 1e-6) ? (sd.x >= 0.0 ? 1e-6 : -1e-6) : sd.x;
                sd.y = (abs(sd.y) < 1e-6) ? (sd.y >= 0.0 ? 1e-6 : -1e-6) : sd.y;
                sd.z = (abs(sd.z) < 1e-6) ? (sd.z >= 0.0 ? 1e-6 : -1e-6) : sd.z;
                float3 invRayDir = rcp(sd); // rcp = fast reciprocal, 1 instr on Adreno

                // AABB slab test (original logic preserved)
                float3 t0 = (-0.5 - ro) * invRayDir;
                float3 t1 = ( 0.5 - ro) * invRayDir;
                float3 tmin3 = min(t0, t1);
                float3 tmax3 = max(t0, t1);

                float tnear = max(max(tmin3.x, tmin3.y), tmin3.z);
                float tfar  = min(min(tmax3.x, tmax3.y), tmax3.z);

                // Original discard conditions preserved exactly
                if (tnear > tfar || tfar < 0.0) discard;
                tnear = max(tnear, 0.0);

                // -- 2. STEP VECTORS -------------------------------------------
                // float3 UVs -- half3 causes ~0.001/step drift -> blocky artifacts
                float3 startUV  = (ro + rayDir * tnear) + 0.5;
                float3 fineStep = ((ro + rayDir * tfar) + 0.5 - startUV) / (float)_Steps;

                // Precompute outside loop (OPT: these were recalculated every iteration)
                float  maxGrid   = max(_GridSizeX, max(_GridSizeY, _GridSizeZ));
                float3 gridRatio = float3(_GridSizeX, _GridSizeY, _GridSizeZ) / maxGrid;
                float3 leftHand  = _LeftHandPos.xyz  * gridRatio;
                float3 rightHand = _RightHandPos.xyz * gridRatio;
                float  clipSq    = (float)_ClipRadius * (float)_ClipRadius;

                // Coarse factor 4 (was 8 -- safer for thin structures like ribs/vessels)
                float3 coarseStep = fineStep * 4.0;

                // OPT: Precompute loop thresholds once
                half earlyExit    = 0.98h / _Intensity; // equiv to original: cumDen*intensity > 0.98
                half coarseThresh = _Threshold * 0.4h;

                // -- 3. RAYMARCH LOOP (original structure preserved) -----------
                float3 uv       = startUV;
                float3 stepVec  = coarseStep;
                bool   isFine   = false;
                half   cumDensity = 0.0h;

                UNITY_LOOP
                for (int j = 0; j < _Steps; j++)
                {
                    if (any(uv < 0.0) || any(uv > 1.0)) break;

                    float3 scaledUV = uv * gridRatio;
                    float3 dL = scaledUV - leftHand;
                    float3 dR = scaledUV - rightHand;

                    if (dot(dL, dL) > clipSq && dot(dR, dR) > clipSq)
                    {
                        half density = tex3Dlod(_VolumeTex, float4(uv, 0)).r;

                        if (!isFine && density > coarseThresh)
                        {
                            // Original coarse->fine transition, preserved exactly:
                            // back up one full coarse step, switch to fine, continue
                            // (the continue skips uv += stepVec so next sample is
                            // exactly at the backed-up position with fine stepping)
                            uv    -= stepVec;
                            stepVec = fineStep;
                            isFine  = true;
                            continue;
                        }

                        if (density >= _Threshold)
                        {
                            cumDensity += density;
                            if (cumDensity > earlyExit) break;
                        }
                    }

                    uv += stepVec;
                }

                // -- 4. OUTPUT -------------------------------------------------
                if (cumDensity < _Threshold) return half4(0, 0, 0, 0);

                half d = saturate(cumDensity * _Intensity);

                half3 rgb = lerp(_mriBlack, _mriSteel, saturate(d * 2.0h));
                rgb        = lerp(rgb, _mriWhite, saturate((d - 0.5h) * 2.0h));

                // OPT: saturate alpha so blend unit gets a clean [0,1] value
                // (original passed raw cumDensity which can exceed 1.0)
                half alpha = saturate(cumDensity * _Intensity);

                return half4(rgb, alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}