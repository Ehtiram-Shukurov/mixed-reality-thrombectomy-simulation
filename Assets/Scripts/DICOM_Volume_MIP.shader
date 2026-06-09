Shader "Custom/DICOM_Volume_MIP_VR_Stable"
{
    Properties
    {
        [Header(Volume Data)]
        _VolumeTex ("Volume Texture (3D)", 3D) = "" {}
        _BrickMap ("Brick Density Map (3D)", 3D) = "" {}
        _GridSizeX ("Grid Size X", Float) = 512
        _GridSizeY ("Grid Size Y", Float) = 512
        _GridSizeZ ("Grid Size Z", Float) = 256
        _SegmentationTex ("Segmentation Texture (3D)", 3D) = "" {}
        _SegmentationBrickMap ("Segmentation Brick Map (3D)", 3D) = "" {}

        _RenderMode ("Shader render mode. (0=DICOM, 1=Full Segmentation, 2=Vascular Segmentation, 3=DICOM with full segmentation, 4=DICOM with vascular segmentation)", Range(0,5)) = 0

        [Header(Raymarching Settings)]
        _Steps ("Raymarch Steps", Range(16, 256)) = 64
        _ColorIntensity ("Color Intensity Multiplier", Range(0.01, 2)) = 0.3
        _AlphaIntensity ("Alpha Intensity Multiplier", Range(0.01, 10)) = 1.0
        _Threshold ("Density Threshold", Range(0.0, 1.0)) = 0.05
        _UpperThreshold ("Upper Density Threshold", Range(0.0, 1.0)) = 1.0
        
        [Header(VR Hand Interaction)]
        _LeftHandPos ("Left Hand Position (UV Space)", Vector) = (0,0,0,0)
        _RightHandPos ("Right Hand Position (UV Space)", Vector) = (0,0,0,0)
        _ClipRadius ("Hand Clip Radius", Range(0.0, 0.5)) = 0.1

        [Header(Colors)]
        _mriBlack ("MRI Black Level", Color) = (0.01, 0.02, 0.04, 1)
        _mriSteel ("MRI Steel Level", Color) = (0.45, 0.52, 0.6, 1)
        _mriWhite ("MRI White Level", Color) = (1.0, 1.0, 1.0, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 100

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        // ZTest Always
        Cull Front

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 4.5

            #include "UnityCG.cginc"

            #define ORGANS (1u << 0)
            #define VASCULATURE (1u << 1)
            #define SKELETON (1u << 2)

            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                // Keep spatial data in 32-bit float for VR stability
                float3 localPos : TEXCOORD0;
                float3 localCamPos : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler3D _VolumeTex;
            sampler3D _BrickMap;
            sampler3D _SegmentationTex;
            sampler3D _SegmentationBrickMap;

            half _GridSizeX, _GridSizeY, _GridSizeZ;
            half _Steps;
            half _ColorIntensity;
            half _AlphaIntensity;
            half _Threshold;
            half _UpperThreshold;
            float4 _LeftHandPos;
            float4 _RightHandPos;
            half _ClipRadius;
            
            half _RenderMode;

            half3 _mriBlack;
            half3 _mriSteel;
            half3 _mriWhite;

            // Dither noise
            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xyz;

                // PERFECT STEREO SYNC: Grab exact eye position in world space
                #if defined(UNITY_SINGLE_PASS_STEREO) || defined(STEREO_INSTANCING_ON) || defined(STEREO_MULTIVIEW_ON)
                    float3 camPos = unity_StereoWorldSpaceCameraPos[unity_StereoEyeIndex];
                #else
                    float3 camPos = _WorldSpaceCameraPos;
                #endif

                // Convert camera directly to Object Space in the vertex shader
                o.localCamPos = mul(unity_WorldToObject, float4(camPos, 1.0)).xyz;

                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                

                // --- 1. SPATIAL MATH ---
                float3 ro = i.localCamPos;
                
                // Guard against the camera being EXACTLY on a vertex before normalizing
                float3 rayDir = i.localPos - ro;
                float lenSq = dot(rayDir, rayDir);
                rayDir *= rsqrt(lenSq);

                // Safe division for AABB (prevents Infinity/NaN issues on some GPUs)
                float3 sd = rayDir;
                sd.x = (abs(sd.x) < 1e-6) ? (sd.x >= 0.0 ? 1e-6 : -1e-6) : sd.x;
                sd.y = (abs(sd.y) < 1e-6) ? (sd.y >= 0.0 ? 1e-6 : -1e-6) : sd.y;
                sd.z = (abs(sd.z) < 1e-6) ? (sd.z >= 0.0 ? 1e-6 : -1e-6) : sd.z;
                float3 invRayDir = rcp(sd);
                
                // AABB Box intersection
                float3 t0 = (-0.5 - ro) * invRayDir;
                float3 t1 = ( 0.5 - ro) * invRayDir;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                
                float tnear = max(max(tmin.x, tmin.y), tmin.z);
                float tfar  = min(min(tmax.x, tmax.y), tmax.z);
                
                // If ray misses box, or box is entirely behind camera, discard
                clip(tfar - max(tnear, 0.0));
                tnear = max(tnear, 0.0);

                // FIXME: this only works for modes 1-3.
                int threshold = _RenderMode == 0 ? _Threshold : 1;

                bool cameraInside = all(abs(ro) < 0.5);
                if (!cameraInside)
                {
                    float tSpan     = tfar - tnear;
                    float brickStep = tSpan / 16.0;

                    UNITY_LOOP
                    for (int b = 0; b < 16; b++)
                    {
                        // Sample the brick map at current t position
                        float3 brickUV = ro + rayDir * (tnear + (float)b * brickStep) + 0.5;
                        half brickDensity = 0.0;
                        if (_RenderMode > 0) {
                            brickDensity = tex3Dlod(_SegmentationBrickMap, float4(brickUV, 0)).r * 117;
                        } else {
                            brickDensity = tex3Dlod(_BrickMap, float4(brickUV, 0)).r;
                        }

                        if (brickDensity > threshold)
                        {
                            // Step back one full brick so we don't clip entry voxels,
                            // but never go behind the original box entry point
                            float newTnear = tnear + ((float)b - 1.0) * brickStep;
                            tnear = max(tnear, newTnear);
                            break;
                        }
                    }
                    if (tnear >= tfar) _Steps = 0;
                }


                float3 startUV = ro + rayDir * tnear + 0.5;
                float3 endUV = ro + rayDir * tfar + 0.5;

                float3 rayDelta = endUV - startUV;
                float3 stepVec = rayDelta / (float)_Steps;
                
                float maxGrid = max(_GridSizeX, max(_GridSizeY, _GridSizeZ));
                float3 gridRatio = half3(_GridSizeX, _GridSizeY, _GridSizeZ) / maxGrid;
                
                float3 leftHand = _LeftHandPos.xyz;
                float3 rightHand = _RightHandPos.xyz;
                float clipSq = _ClipRadius * _ClipRadius; 

                float3 uv = startUV;

                half dither = hash(i.pos.xy);
                uv += stepVec * (dither * 0.01);

                // --- 2. TEXTURE LOOP (Safe to use 16-bit Half for Speed) ---
                half cumulative_density = 0.0;
                // float courseFactor = 8.0;
                // float3 currentStepVec = stepVec * courseFactor;
                // bool isFineStepping = false;

                half earlyExit = 0.9h / _ColorIntensity;

                UNITY_LOOP
                for (int j = 0; j < _Steps; j++)
                {
                    float3 diffL = uv - leftHand;
                    float3 diffR = uv - rightHand;
                    bool inClipRegion = (dot(diffL, diffL) <= clipSq || dot(diffR, diffR) <= clipSq);

                    if (!inClipRegion)
                    {
                        if (_RenderMode == 6) 
                        {
                            float rawDensity = tex3Dlod(_VolumeTex, float4(uv, 0)).r;
                            float segRaw = tex3Dlod(_SegmentationTex, float4(uv, 0)).r;
                            int label = round(segRaw * 117.0);
        
                            float densityToAdd = 0.0;

                            if (label >= 22 && label <= 29) {
                                densityToAdd = 1.0; 
                            } 
                            else if (rawDensity >= 0.73 && rawDensity <= 1.0) {
                                densityToAdd = rawDensity *0.1;
                            }
        
                            cumulative_density += densityToAdd;
                            if (cumulative_density > earlyExit) break;
                        }
                        else if (_RenderMode == 0) {
                            float density = tex3Dlod(_VolumeTex, float4(uv, 0)).r;
                            if (density >= _Threshold && density <= _UpperThreshold)
                            {
                                // if (density > cumulative_density) {
                                //     cumulative_density = density;
                                // }
                                cumulative_density += density;
                                if (cumulative_density > earlyExit) break;
                            }
                        }
                        else if (_RenderMode == 1)
                        {
                            float density = round(tex3Dlod(_SegmentationTex, float4(uv, 0)).r * 117);
                            if (density > 0 && density < 40) {
                                cumulative_density = density;
                                break;
                            }
                        }
                        else if (_RenderMode == 2)
                        {
                            float density = round(tex3Dlod(_SegmentationTex, float4(uv, 0)).r * 117);
                            if (density > 21 && density < 30) {
                                cumulative_density = density;
                                break;
                            }
                        }
                        else if (_RenderMode == 3)
                        {
                            float density = round(tex3Dlod(_SegmentationTex, float4(uv, 0)).r * 117);
                            if (density > 17 && density < 40) {
                                cumulative_density = density;
                                break;
                            }
                        }
                        else if (_RenderMode == 4)
                        {
                            float density = round(tex3Dlod(_SegmentationTex, float4(uv, 0)).r * 117);
                            if ((density > 0 && density < 13) || (density > 18 && density < 30)) {
                                cumulative_density = density;
                                break;
                            }
                        }
                        else if (_RenderMode == 5)
                        {
                            float density = round(tex3Dlod(_SegmentationTex, float4(uv, 0)).r * 117);
                            if ((density > 21 && density < 30) || (density == 2) || (density == 3) || (density == 10) || (density == 11)) {
                                cumulative_density = density;
                                break;
                            }
                        }
                    } else {
                        cumulative_density = -0.01;
                    }

                    uv += stepVec;
                }

                if (_RenderMode == 0 || _RenderMode == 6)
                {
                    half activeThreshold = (_RenderMode == 6) ? 0.001 : 0.001;
                    clip(cumulative_density - activeThreshold);
    
                    half activeIntensity = (_RenderMode == 6) ? 2.5 : _ColorIntensity;
                    float d = saturate(cumulative_density * activeIntensity);

                    fixed3 finalRGB = lerp(_mriBlack, _mriSteel, saturate(d * 2.0));
                    finalRGB = lerp(finalRGB, _mriWhite, saturate((d - 0.5) * 2.0));

                    return fixed4(finalRGB, saturate(cumulative_density * _AlphaIntensity));
                }
                else
                {
                    clip(cumulative_density - 0.001);
                    uint labelId = (uint)cumulative_density;

                    // Derive category from label ranges (mirrors your mode logic)
                    float hueBase;
                    if      (labelId < 13)                          hueBase = 0.05; // organs      → warm orange/red
                    else if (labelId >= 22 && labelId < 30)         hueBase = 0.62; // vasculature → blue/cyan
                    else if (labelId >= 13 && labelId < 22)         hueBase = 0.13; // bone/spine  → yellow
                    else                                            hueBase = 0.35; // other       → green fallback

                    // Per-label variation within the band
                    float h = frac(sin(labelId * 127.1) * 43758.5453);
                    float v = frac(sin(labelId * 311.7) * 53758.5453);

                    float hue = hueBase + h * 0.12;
                    float sat  = 0.65 + v * 0.25;  // 0.65–0.90
                    float val  = 0.75 + v * 0.20;  // 0.75–0.95, keep bright since no lighting

                    // HSV → RGB
                    float3 rgb = val * lerp(float3(1,1,1), saturate(abs(frac(hue + float3(0, 0.667, 0.333)) * 6.0 - 3.0) - 1.0), sat);

                    return fixed4(rgb, 1);
                }

            }
            ENDCG
        }
    }
}