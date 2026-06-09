using UnityEngine;
using UnityEngine.Networking;
using FellowOakDicom;
using FellowOakDicom.Imaging;
using FellowOakDicom.Log;
using System.IO;
using System.IO.Compression; 
using System.Linq;
using System;
using System.Collections;
using Microsoft.Extensions.DependencyInjection;
using FellowOakDicom.Imaging.Codec;
using Oculus.Interaction;
using Oculus.Interaction.Input;

public class DicomVolumeLoader : MonoBehaviour
{
    [Header("DICOM Settings")]
    public string dicomZipFilename = "series-000003.zip";

    public string segmentedZipFilename = "segmentation.nii.gz.bytes";
    
    [Tooltip("Check this, run once, then uncheck it to clear corrupted local files.")]
    public bool forceCleanCache = false; 

    private string _internalExtractionPath;
    private string _zipDestinationPath;

    [Header("Rendering")]
    public Material volumeMaterial;
    public Texture3D _volumeTexture;
    private Texture3D _brickMap;

    public Texture3D _segmentationTexture;
    private Texture3D _segmentationBrickMap;
    public float threshold = 0.15f;

    [Header("Interactions")]
    public Hand leftHand;
    public Hand rightHand;

    void Start()
    {
        Debug.Log($"=== DicomVolumeLoader Start ===");
        Debug.Log($"dicomZipFilename: '{dicomZipFilename}'");
        Debug.Log($"segmentedZipFilename: '{segmentedZipFilename}'");
        Debug.Log($"volumeMaterial: {(volumeMaterial != null ? volumeMaterial.name : "NULL")}");

        _internalExtractionPath = Path.Combine(Application.persistentDataPath, "ExtractedDICOM");
        _zipDestinationPath = Path.Combine(Application.persistentDataPath, dicomZipFilename);

        // Debugging Tool: Wipes the local Quest/PC cache if things get stuck
        if (forceCleanCache)
        {
            if (File.Exists(_zipDestinationPath)) File.Delete(_zipDestinationPath);
            if (Directory.Exists(_internalExtractionPath)) Directory.Delete(_internalExtractionPath, true);
            Debug.LogWarning("CACHE CLEARED. Uncheck 'Force Clean Cache' in the inspector now.");
        }

        try 
        {
            new DicomSetupBuilder()
                .RegisterServices(s => s
                    .AddFellowOakDicom()
                    .AddImageManager<RawImageManager>())
                .Build();
        }
        catch (Exception e) 
        {
            Debug.LogError("Error initializing DICOM: " + e.Message);
        }

        StartCoroutine(ExtractAndLoadDICOM());
        Debug.Log($"Started DICOM loading coroutine with zip: {dicomZipFilename}");
        Debug.Log($"Segmented zip filename: {segmentedZipFilename}");
        if (segmentedZipFilename != null && segmentedZipFilename != "")
        {
            StartCoroutine(ExtractAndLoadSegmented());
        }
    }

    public void UpdateLowerThreshold(float newThreshold)
    {
        threshold = newThreshold;
        if (volumeMaterial != null)
        {
            volumeMaterial.SetFloat("_Threshold", threshold);
        }
    }

    public void UpdateUpperThreshold(float newThreshold)
    {
        if (volumeMaterial != null)
        {
            volumeMaterial.SetFloat("_UpperThreshold", newThreshold);
        }
    }

    public void UpdateSegmentationMode(int mode)
    {
        if (volumeMaterial != null)
        {
            volumeMaterial.SetInt("_RenderMode", mode);
        }
    }

    IEnumerator ExtractAndLoadDICOM()
    {
        string zipSourcePath = Path.Combine(Application.streamingAssetsPath, dicomZipFilename);

        // 1. Copy the ZIP file to persistentDataPath (if not already there)
        if (!File.Exists(_zipDestinationPath))
        {
            Debug.Log("Copying DICOM zip to persistent storage...");

            if (Application.platform == RuntimePlatform.Android)
            {
                using (UnityWebRequest www = UnityWebRequest.Get(zipSourcePath))
                {
                    // This streams the file directly to disk, saving massive amounts of Quest RAM
                    www.downloadHandler = new DownloadHandlerFile(_zipDestinationPath);
                    yield return www.SendWebRequest();

                    if (www.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError("Failed to copy zip on Android: " + www.error);
                        yield break; 
                    }
                }
            }
            else
            {
                try
                {
                    File.Copy(zipSourcePath, _zipDestinationPath);
                }
                catch (Exception e)
                {
                    Debug.LogError("Failed to copy zip on PC: " + e.Message);
                    yield break;
                }
                yield return null; 
            }
            Debug.Log("Successfully copied zip.");
        }

        // 2. Check if we need to unzip by looking for actual .dcm files
        bool needsExtraction = true;
        if (Directory.Exists(_internalExtractionPath))
        {
            string[] existingFiles = Directory.GetFiles(_internalExtractionPath, "*.dcm", SearchOption.AllDirectories);
            if (existingFiles.Length > 0)
            {
                needsExtraction = false;
            }
        }

        // 3. Unzip if necessary
        if (needsExtraction)
        {
            Debug.Log("Unzipping DICOM files...");
            
            if (Directory.Exists(_internalExtractionPath))
            {
                Directory.Delete(_internalExtractionPath, true);
            }
            Directory.CreateDirectory(_internalExtractionPath);
            
            try
            {
                ZipFile.ExtractToDirectory(_zipDestinationPath, _internalExtractionPath);
                Debug.Log("Unzipping complete.");
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to unzip: " + e.Message);
                yield break;
            }
        }
        else
        {
            Debug.Log("DICOM files already extracted. Skipping unzip.");
        }

        // 4. Load the files
        yield return StartCoroutine(LoadDICOMFolderCoroutine());
    }

    IEnumerator LoadDICOMFolderCoroutine() 
    {
        string[] filePaths = Directory.GetFiles(_internalExtractionPath, "*.dcm", SearchOption.AllDirectories);
        
        if (filePaths.Length == 0) 
        {
            Debug.LogError("No DICOM files found in path: " + _internalExtractionPath);
            yield break;
        }

        Debug.Log("Sorting files...");
        DicomFile[] sortedFiles = filePaths
            .Select(path => DicomFile.Open(path))
            .OrderBy(file => file.Dataset.GetSingleValueOrDefault<int>(DicomTag.InstanceNumber, 0))
            .ToArray();
        
        sortedFiles = sortedFiles[0..210];

        var firstFile = sortedFiles[0];
        int width = firstFile.Dataset.GetSingleValue<int>(DicomTag.Columns);
        int height = firstFile.Dataset.GetSingleValue<int>(DicomTag.Rows);
        int depth = sortedFiles.Length;

        // Fix scaling
        double[] pixelSpacing = firstFile.Dataset.GetValues<double>(DicomTag.PixelSpacing);
        double spacingX = pixelSpacing.Length > 0 ? pixelSpacing[0] : 1.0;
        double spacingY = pixelSpacing.Length > 1 ? pixelSpacing[1] : 1.0;
        double spacingZ = firstFile.Dataset.GetSingleValueOrDefault<double>(DicomTag.SliceThickness, 1.0);

        float physicalWidth = (float)(width * spacingX);
        float physicalHeight = (float)(height * spacingY);
        float physicalDepth = (float)(depth * spacingZ);

        float maxDim = Mathf.Max(physicalWidth, physicalHeight, physicalDepth);
        
        float desiredVRSize = 0.5f; 
        transform.localScale = new Vector3(physicalWidth / maxDim, physicalHeight / maxDim, physicalDepth / maxDim) * desiredVRSize;

        _volumeTexture = new Texture3D(width, height, depth, TextureFormat.R16, false);
        _volumeTexture.wrapMode = TextureWrapMode.Clamp;
        _volumeTexture.filterMode = FilterMode.Point;

        ushort[] volumeData = new ushort[width * height * depth];
        int sliceSize = width * height;

        Debug.Log("Building 3D Texture...");

        // --- SPEED OPTIMIZATION: Lookup Tables (LUT) ---
        // Precalculate all possible DICOM values once so we don't do float math millions of times
        ushort[] signedLUT = new ushort[65536];
        for (int i = -32768; i <= 32767; i++)
        {
            float hounsfield = Mathf.Clamp(i, -1000f, 2000f);
            float normalized = (hounsfield + 1000f) / 3000f;
            signedLUT[(ushort)(short)i] = (ushort)(normalized * 65535f);
        }

        ushort[] unsignedLUT = new ushort[65536];
        for (int i = 0; i < 65536; i++)
        {
            float val = Mathf.Clamp(i, 0f, 4000f);
            float normalized = val / 4000f;
            unsignedLUT[i] = (ushort)(normalized * 65535f);
        }
        // ------------------------------------------------
        ushort[] finalSlicePixels = new ushort[sliceSize];
        for (int z = 0; z < depth; z++) 
        {
            var file = sortedFiles[z]; 
            var pixelData = DicomPixelData.Create(file.Dataset);
            var frame = pixelData.GetFrame(0);
            
            bool isSigned = file.Dataset.GetSingleValueOrDefault(DicomTag.PixelRepresentation, 0) == 1;
            
            if (isSigned)
            {
                short[] signedPixels = FellowOakDicom.IO.ByteConverter.ToArray<short>(frame);
                for (int i = 0; i < sliceSize; i++)
                {
                    int x = (i % width);
                    int y = height - 1 - (i / width); // flip Y
                    int flippedIndex = y * width + x;
                    finalSlicePixels[flippedIndex] = signedLUT[(ushort)signedPixels[i]];
                }
            }
            else
            {
                ushort[] unsignedPixels = FellowOakDicom.IO.ByteConverter.ToArray<ushort>(frame);
                for (int i = 0; i < sliceSize; i++)
                {
                    int x = (i % width);
                    int y = height - 1 - (i / width); // flip Y
                    int flippedIndex = y * width + x;
                    finalSlicePixels[flippedIndex] = unsignedLUT[unsignedPixels[i]];
                }
            }
            int flippedZ = depth - 1 - z; // Flip Z to match Unity's coordinate system
            Array.Copy(finalSlicePixels, 0, volumeData, flippedZ * sliceSize, sliceSize);

            // ONLY pause the extraction if we are running on the Android headset
            if (Application.platform == RuntimePlatform.Android && z % 5 == 0)
            {
                yield return null; 
            }
        }

        _volumeTexture.SetPixelData(volumeData, 0);
        _volumeTexture.Apply();

        if (volumeMaterial != null) 
        {
            volumeMaterial.SetTexture("_VolumeTex", _volumeTexture);
            volumeMaterial.SetTexture("_BrickMap", BuildBrickMap(volumeData, width, height, depth, 16));
            volumeMaterial.SetVector("_VoxelSpacing", new Vector3((float)spacingX, (float)spacingY, (float)spacingZ));
            volumeMaterial.SetFloat("_GridSizeX", width);
            volumeMaterial.SetFloat("_GridSizeY", height);
            volumeMaterial.SetFloat("_GridSizeZ", depth);
            Debug.Log($"Successfully built texture: {width}x{height}x{depth}");
        }
    }

    Texture3D BuildBrickMap(ushort[] volumeData, int width, int height, int depth, int brickSize, bool segment_mode=false)
    {
        int brickCountX = Mathf.CeilToInt((float)width / brickSize);
        int brickCountY = Mathf.CeilToInt((float)height / brickSize);
        int brickCountZ = Mathf.CeilToInt((float)depth / brickSize);

        Texture3D brickMap = new Texture3D(brickCountX, brickCountY, brickCountZ, TextureFormat.R16, false);
        ushort[] brickData = new ushort[brickCountX * brickCountY * brickCountZ];

        for (int z = 0; z < depth; z += brickSize)
        {
            for (int y = 0; y < height; y += brickSize)
            {
                for (int x = 0; x < width; x += brickSize)
                {
                    ushort maxVal = 0;
                    for (int bz = 0; bz < brickSize; bz++)
                    {
                        for (int by = 0; by < brickSize; by++)
                        {
                            for (int bx = 0; bx < brickSize; bx++)
                            {
                                int vx = x + bx;
                                int vy = y + by;
                                int vz = z + bz;
                                if (vx < width && vy < height && vz < depth)
                                {
                                    ushort val = volumeData[vz * width * height + vy * width + vx];
                                    if (!segment_mode)
                                    {
                                        if (val > maxVal) maxVal = val;   
                                    }
                                    else
                                    {
                                        if (val > 0 && val < 40)
                                        {
                                            maxVal = 500;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    int brickIndex = (z / brickSize) * (brickCountX * brickCountY) + (y / brickSize) * brickCountX + (x / brickSize);
                    brickData[brickIndex] = maxVal;
                }
            }
        }

        brickMap.SetPixelData(brickData, 0);
        brickMap.wrapMode = TextureWrapMode.Clamp;
        brickMap.filterMode = FilterMode.Point;
        brickMap.Apply();
        return brickMap;
    }

    static void ReadFully(Stream stream, byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = stream.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0) throw new EndOfStreamException($"Stream ended after {totalRead}/{count} bytes");
            totalRead += read;
        }
    }


    IEnumerator ExtractAndLoadSegmented()
    {
        string zipSourcePath = Path.Combine(Application.streamingAssetsPath, segmentedZipFilename);
        string destFilename = segmentedZipFilename.Replace(".bytes", "");
        string destPath = Path.Combine(Application.persistentDataPath, destFilename);
        Debug.Log("Extracting from: " + zipSourcePath);
        if (!File.Exists(destPath) || new FileInfo(destPath).Length < 500)
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                using UnityWebRequest www = UnityWebRequest.Get(zipSourcePath);
                www.downloadHandler = new DownloadHandlerFile(destPath);
                yield return www.SendWebRequest();
                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogError("Failed to copy segmentation file: " + www.error);
                    yield break;
                }
            }
            else
            {
                File.Copy(zipSourcePath, destPath);
            }
        }

        Debug.Log("About to read file");

        byte[] header = new byte[352]; // NIfTI header is always 352 bytes
        int height, width, depth;
        ushort[] segmentedData;
        using (FileStream fs = File.OpenRead(destPath))
        using (GZipStream gz = new GZipStream(fs, CompressionMode.Decompress))
        {
            Debug.Log("about to read file");
            ReadFully(gz, header, 0, 352);

            float voxOffset = BitConverter.ToSingle(header, 108);
            short datatype  = BitConverter.ToInt16(header, 70);

            // Skip to voxel data start
            int skipBytes = (int)voxOffset - 352;
            if (skipBytes > 0)
            {
                byte[] skip = new byte[skipBytes];
                ReadFully(gz, skip, 0, skipBytes);
            }

            width = BitConverter.ToInt16(header, 42);
            height = BitConverter.ToInt16(header, 44);
            depth = BitConverter.ToInt16(header, 46);

            Debug.Log($"Segmented Volume Dimensions: {width}x{height}x{depth}, Datatype: {datatype}, Voxel Offset: {voxOffset}");

            int totalPixels = width * height * depth;
            int bytesPerVoxel = (datatype == 4) ? 2 : 1; // INT16 vs UINT8
            byte[] voxelBytes = new byte[totalPixels * bytesPerVoxel];
            ReadFully(gz, voxelBytes, 0, voxelBytes.Length);

            segmentedData = new ushort[totalPixels];
            for (int z = 0; z < depth; z++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int idx = z * width * height + y * width + x;

                        float label = (bytesPerVoxel == 2)
                            ? BitConverter.ToInt16(voxelBytes, idx * 2)
                            : voxelBytes[idx];

                        segmentedData[idx] = (ushort)((label / 117f) * 65535f);
                    }
                }

                if (z % 5 == 0)
                {
                    Debug.Log($"Processed segmentation slice {z}/{depth}");
                    yield return null; // Pause every 5 slices to keep UI responsive, especially on Quest
                }
            }
        }

        float spacingX = BitConverter.ToSingle(header, 80);
        float spacingY = BitConverter.ToSingle(header, 84);
        float spacingZ = BitConverter.ToSingle(header, 88);

        _segmentationTexture = new Texture3D(width, height, depth, TextureFormat.R16, false);
        _segmentationTexture.wrapMode = TextureWrapMode.Clamp;
        _segmentationTexture.filterMode = FilterMode.Point;
        _segmentationTexture.SetPixelData(segmentedData, 0);
        _segmentationTexture.Apply();

        if (volumeMaterial != null) 
        {
            volumeMaterial.SetTexture("_SegmentationTex", _segmentationTexture);
            volumeMaterial.SetTexture("_SegmentationBrickMap", BuildBrickMap(segmentedData, width, height, depth, 16, true));
        }

        yield return null;
    }

    void Update()
    {
        if (volumeMaterial != null)
        {
            if (leftHand != null)
            {
                if (leftHand.GetJointPose(HandJointId.HandIndex3, out Pose leftPalmPose))
                {
                    Vector3 localPose = transform.InverseTransformPoint(leftPalmPose.position);
                    Vector3 uvwPos = new Vector3(localPose.x + 0.5f, localPose.y + 0.5f, localPose.z + 0.5f);
                    volumeMaterial.SetVector("_LeftHandPos", uvwPos);  
                }   
            }
            if (rightHand != null)
            {
                if(rightHand.GetJointPose(HandJointId.HandIndex3, out Pose rightPalmPose))
                {
                    Vector3 localPose = transform.InverseTransformPoint(rightPalmPose.position);
                    Vector3 uvwPos = new Vector3(localPose.x + 0.5f, localPose.y + 0.5f, localPose.z + 0.5f);
                    volumeMaterial.SetVector("_RightHandPos", uvwPos);
                }
            }
        }
    }
}