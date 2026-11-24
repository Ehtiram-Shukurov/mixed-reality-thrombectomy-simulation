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
        _Contrast ("Vessel contrast in X-ray mode (0 = none, 1 = full)", Range(0.0, 1.0)) = 1.0
        _DepthFade ("Darken far surfaces in vessel modes (0 = off)", Range(0.0, 1.0)) = 0.35
        _Shading ("Surface lighting in vessel modes (0 = off, 1 = on)", Range(0.0, 1.0)) = 1.0
        _Jitter ("Ray start jitter in steps (0.01 = almost off, 0.5 = trade banding for noise)", Range(0.0, 1.0)) = 0.01
        [Header(Mode 7 X ray projection for the monitor)]
        _DrrLow ("CT value treated as air (0 to 1)", Range(0.0, 1.0)) = 0.35
        _DrrGain ("Absorption strength", Range(0.1, 40.0)) = 6.0
        _DrrDye ("Extra absorption of dye", Range(0.0, 10.0)) = 2.0

        [Header(VR Hand Interaction)]
        _LeftHandPos ("Left Hand Position (UV Space)", Vector) = (0,0,0,0)
        _RightHandPos ("Right Hand Position (UV Space)", Vector) = (0,0,0,0)
        _ClipRadius ("Hand Clip Radius", Range(0.0, 0.5)) = 0.1

        [Header(Colors)]
        _mriBlack ("MRI Black Level", Color) = (0.01, 0.02, 0.04, 1)
        _mriSteel ("MRI Steel Level", Color) = (0.45, 0.52, 0.6, 1)
        _mriWhite ("MRI White Level", Color) = (1.0, 1.0, 1.0, 1)
        _VesselColor ("Vessel Color", Color) = (0.74, 0.16, 0.18, 1)
        _OrganColor ("Organ Color", Color) = (0.78, 0.60, 0.50, 1)
        _BoneColor ("Bone Color", Color) = (0.90, 0.86, 0.76, 1)
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
            half _Contrast;
            // Dye map set by DyeFlow as globals (deliberately not in Properties).
            // Each cell holds the distance along the vessels from the heart end; 10000 = no flow.
            sampler3D _VolDyeTex;
            float _VolDyeOn, _VolDyeFront, _VolDyeFeather, _VolDyePlain;
            half _DepthFade;
            half _Shading;
            half _Jitter;
            float _DrrLow, _DrrGain, _DrrDye;
            float4 _LeftHandPos;
            float4 _RightHandPos;
            half _ClipRadius;
            
            half _RenderMode;

            half3 _mriBlack;
            half3 _mriSteel;
            half3 _mriWhite;
            half4 _VesselColor;
            half4 _OrganColor;
            half4 _BoneColor;

            // Dither noise
            float hash(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            // 1 if the label at this point belongs to the current segmentation mode, else 0.
            // Mirrors the label ranges in the main loop.
            float Solid(float3 p)
            {
                float d = round(tex3Dlod(_SegmentationTex, float4(p, 0)).r * 117.0);
                bool inside = false;
                if (_RenderMode == 1)      inside = d > 0 && d < 40;
                else if (_RenderMode == 2) inside = d > 21 && d < 30;
                else if (_RenderMode == 3) inside = d > 17 && d < 40;
                else if (_RenderMode == 4) inside = (d > 0 && d < 13) || (d > 18 && d < 30);
                else                       inside = (d > 21 && d < 30) || d == 2 || d == 3 || d == 10 || d == 11;
                return inside ? 1.0 : 0.0;
            }

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
                // Mode 7 adds up all tissue, so it must not skip ahead to the first labelled brick.
                if (!cameraInside && _RenderMode != 7)
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
                uv += stepVec * (dither * _Jitter);

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
                                // Dye in the vessels. With no contrast they barely show, as on a plain X-ray.
                                // During a dye run, a vessel lights up only once the front has reached it,
                                // so the dye stops at the clot here too.
                                float c = _Contrast;
                                if (_VolDyeOn > 0.5)
                                {
                                    float dd = tex3Dlod(_VolDyeTex, float4(uv, 0)).r;
                                    float reach = saturate((_VolDyeFront - dd) / max(_VolDyeFeather, 1e-4));
                                    c = lerp(_VolDyePlain, _Contrast, reach);
                                }
                                densityToAdd = c;
                            } 
                            else if (rawDensity >= 0.73 && rawDensity <= 1.0) {
                                densityToAdd = rawDensity *0.1;
                            }
        
                            cumulative_density += densityToAdd;
                            if (cumulative_density > earlyExit) break;
                        }
                        else if (_RenderMode == 7)
                        {
                            // X-ray projection: add up how much each sample absorbs along the whole ray.
                            // CT value above the air level counts as tissue; dye in reached vessels adds more.
                            float raw = tex3Dlod(_VolumeTex, float4(uv, 0)).r;
                            float mu = saturate((raw - _DrrLow) / max(1e-3, 1.0 - _DrrLow));
                            float segRaw7 = tex3Dlod(_SegmentationTex, float4(uv, 0)).r;
                            int label7 = round(segRaw7 * 117.0);
                            if (label7 >= 22 && label7 <= 29)
                            {
                                float c7 = _Contrast;
                                if (_VolDyeOn > 0.5)
                                {
                                    float dd7 = tex3Dlod(_VolDyeTex, float4(uv, 0)).r;
                                    float reach7 = saturate((_VolDyeFront - dd7) / max(_VolDyeFeather, 1e-4));
                                    c7 = lerp(_VolDyePlain, _Contrast, reach7);
                                }
                                mu += c7 * _DrrDye;
                            }
                            cumulative_density += mu;
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

                if (_RenderMode == 7)
                {
                    // Beer-Lambert: transmitted light falls off exponentially with total absorption.
                    // Output the absorbed share; the monitor inverts it so dense reads dark.
                    float absorbed = 1.0 - exp(-_DrrGain * cumulative_density * length(stepVec));
                    return fixed4(absorbed.xxx, 1);
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
                    // A fixed, muted palette per category instead of random hues.
                    // Vessels deep red, organs soft tan, bone ivory, anything else grey-green.
                    float3 baseCol;
                    if      (labelId >= 22 && labelId < 30) baseCol = _VesselColor.rgb;
                    else if (labelId < 13)                  baseCol = _OrganColor.rgb;
                    else if (labelId >= 13 && labelId < 22) baseCol = _BoneColor.rgb;
                    else                                    baseCol = float3(0.55, 0.62, 0.58);
                    hueBase = 0;

                    // Small brightness change per label so neighbouring structures stay apart.
                    float v = frac(sin(labelId * 311.7) * 53758.5453);
                    float3 rgb = baseCol * (0.86 + v * 0.22);

                    // The loop steps are coarse, so narrow the hit down to the surface between
                    // the last empty sample and the first solid one (3 reads).
                    float3 hitUV = uv;
                    if (_Shading > 0.5)
                    {
                        float3 a = uv - stepVec;
                        float3 b = uv;
                        UNITY_UNROLL
                        for (int r = 0; r < 3; r++)
                        {
                            float3 m = (a + b) * 0.5;
                            if (Solid(m) > 0.5) b = m; else a = m;
                        }
                        hitUV = b;
                    }

                    // Surface direction from how the hit point changes between neighbouring
                    // pixels (no texture reads). Kept outside any branch so the derivatives are valid.
                    float3 dx = ddx(hitUV);
                    float3 dy = ddy(hitUV);
                    float3 n = cross(dx, dy);
                    float nLen = length(n);
                    // At silhouettes the neighbour pixel hits something far away. Fall back to
                    // facing the viewer there, so the edge does not sparkle.
                    float jump = max(length(dx), length(dy));
                    float pixelStep = length(stepVec);
                    bool smooth = nLen > 1e-10 && jump < pixelStep * 1.5;
                    n = smooth ? n / nLen : -rayDir;
                    if (dot(n, -rayDir) < 0) n = -n;

                    if (_Shading > 0.5)
                    {
                        // Soft light from the viewer. No shiny highlight: on voxel data it
                        // turns the steps into bright scratches.
                        float ndl = saturate(dot(n, -rayDir));
                        // Gentle, so the voxel steps in this data do not show as scratches.
                        rgb *= 0.62 + 0.38 * ndl;
                    }

                    // Depth cue, no extra texture reads: surfaces farther along the ray are darker.
                    float spanLen = max(length(rayDelta), 1e-5);
                    float depthFrac = saturate(length(hitUV - startUV) / spanLen);
                    rgb *= lerp(1.0, 1.0 - _DepthFade, depthFrac);

                    return fixed4(rgb, 1);
                }

            }
            ENDCG
        }
    }
}