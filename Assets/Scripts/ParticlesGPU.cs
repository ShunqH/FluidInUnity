using System.Net;
using System.Threading.Tasks;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using static UnityEditor.PlayerSettings;
using static UnityEngine.Mathf;
using static UnityEngine.ParticleSystem;

public class ParticleSimulationGPU : MonoBehaviour
{
    public BoxController boundary;
    public ComputeShader SPHComputeShader;
    public Shader RenderShader; 
    [Header("General")]
    public float gravity = 9.8f;
    [Header("Box")]
    [Range(0f, 1f)]
    public float CollisionDamping = 0.8f;
    [Range(0f, 1f)]
    public float ShearDamping = 0.95f;
    [Header("Particle Properties")]
    public int particleCount = 100;
    public float particleMass = 0.1f;
    public float smoothLength = 0.2f;
    public float density0 = 1f;
    public float soundSpeed = 1f;
    public float velocityConstraint = 0.5f;
    [Header("Artificial Viscosity")]
    public float alpha = 1f;
    public float beta = 2f;
    [Header("Visualization")]
    public float VisualizationMin = 0f;
    public float VisualizationMax = 1f;
    public Color ColorMin = Color.blue;
    public Color ColorMax = Color.red;

    private float dt = 0.01667f;
    private const float PI = 3.1415926535f;
    private int dispatchX = 64;
    private int THREADS_PER_GROUP_X = 512;
    private Vector3[] positions;
    private Vector3[] velocities;
    private Vector3[] accelerations;
    private float[] density;
    private float[] mass;
    private Vector3 gravityVector;
    private float particleSize;
    private float maxSpeed;
    private int[] blockIDs;
    private int[] sortedIDs;
    private int[] blockStart;
    private int[] counts;
    private int[] blockOffset;
    private int nBlocks;

    private int kernelDensity, kernelAcceleration, kernelUpdate;


    private ComputeBuffer posBuffer, velBuffer, accBuffer, denBuffer, masBuffer,
                blockIDsBuffer, sortedIDsBuffer, blockStartBuffer, blockOffsetBuffer; 
    //private ComputeBuffer debugBuffer; 
    private Material renderMat;

    

    private void Awake()
    {
        Application.targetFrameRate = 60;
        gravityVector = new Vector3(0, -gravity, 0);
        maxSpeed = velocityConstraint * soundSpeed;
        particleSize = 0.2f * smoothLength;
        positions = new Vector3[particleCount];
        velocities = new Vector3[particleCount];
        accelerations = new Vector3[particleCount];
        density = new float[particleCount];
        mass = new float[particleCount];
        blockIDs = new int[particleCount];
        sortedIDs = new int[particleCount];
        blockOffset = new int[27];
        boundary.UpdateBounds();

        kernelUpdate = SPHComputeShader.FindKernel("kernelUpdate");
        kernelDensity = SPHComputeShader.FindKernel("kernelDensity");
        kernelAcceleration = SPHComputeShader.FindKernel("kernelAcceleration");
        dispatchX = CeilToInt((float)particleCount / THREADS_PER_GROUP_X);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        posBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
        velBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
        accBuffer = new ComputeBuffer(particleCount, sizeof(float) * 3);
        denBuffer = new ComputeBuffer(particleCount, sizeof(float));
        masBuffer = new ComputeBuffer(particleCount, sizeof(float));
        blockIDsBuffer = new ComputeBuffer(particleCount, sizeof(int));
        sortedIDsBuffer = new ComputeBuffer(particleCount, sizeof(int));
        blockOffsetBuffer = new ComputeBuffer(27, sizeof(int)); 
        renderMat =  new Material(RenderShader);
        //debugBuffer = new ComputeBuffer(1, sizeof(float));

        Initialization(); 

        //SPHComputeShader.SetBuffer(kernelUpdate, "debug", debugBuffer);
        //debugBuffer.SetData(new float[] { 0f });

        Graphics.DrawProcedural(renderMat, 
                                new Bounds(Vector3.zero, Vector3.one * 1000),
                                MeshTopology.Points, particleCount);
    }

    // Update is called once per frame
    void Update()
    {
        dt = Time.deltaTime;
        //posBuffer.GetData(positions); 
        BuildBlocks();
        SetCBuffer();

        SPHComputeShader.Dispatch(kernelDensity, dispatchX, 1, 1);
        SPHComputeShader.Dispatch(kernelAcceleration, dispatchX, 1, 1);
        SPHComputeShader.Dispatch(kernelUpdate, dispatchX, 1, 1);

        AsyncGPUReadback.Request(posBuffer, (req) =>
        {
            if (!req.hasError)
                positions = req.GetData<Vector3>().ToArray();
        });

        //float[] debug = new float[1];
        //debugBuffer.GetData(debug);
        //Debug.Log("debug: " + debug[0]);

        Graphics.DrawProcedural(renderMat,
                                new Bounds(Vector3.zero, Vector3.one * 1000),
                                MeshTopology.Points, particleCount);
    }


    // block setup for neighbor search
    void BuildBlocks()
    {
        float blockSize = 2.5f * smoothLength;
        int nx = (int)((boundary.maxBound.x - boundary.minBound.x) / blockSize) + 1;
        int ny = (int)((boundary.maxBound.y - boundary.minBound.y) / blockSize) + 1;
        int nz = (int)((boundary.maxBound.z - boundary.minBound.z) / blockSize) + 1;
        nBlocks = nx * ny * nz;

        GetBlockOffset(nx, ny);
        if (blockStart == null || blockStart.Length < nBlocks + 1) blockStart = new int[nBlocks + 1];
        else for (int i = 0; i < nBlocks + 1; i++) blockStart[i] = 0;
        if (counts == null || counts.Length < nBlocks) counts = new int[nBlocks];
        else for (int i = 0; i < nBlocks; i++) counts[i] = 0;

        for (int pt = 0; pt < particleCount; pt++)
        {
            int i = (int)((positions[pt].x - boundary.minBound.x) / blockSize);
            int j = (int)((positions[pt].y - boundary.minBound.y) / blockSize);
            int k = (int)((positions[pt].z - boundary.minBound.z) / blockSize);
            i = Clamp(i, 0, nx - 1);
            j = Clamp(j, 0, ny - 1);
            k = Clamp(k, 0, nz - 1);
            blockIDs[pt] = i + j * nx + k * nx * ny;
            counts[blockIDs[pt]]++;
        }
        blockStart[0] = 0;
        for (int n = 0; n < nBlocks; n++)
        {
            blockStart[n + 1] = blockStart[n] + counts[n];
        }
        for (int pt = 0; pt < particleCount; pt++)
        {
            int id = blockIDs[pt];
            sortedIDs[blockStart[id + 1] - counts[id]] = pt;
            counts[id]--;
        }

        if (blockStartBuffer == null || blockStartBuffer.count < nBlocks + 1)
        {
            if (blockStartBuffer != null) blockStartBuffer.Release();
            blockStartBuffer = new ComputeBuffer(nBlocks + 1, sizeof(int));
            SPHComputeShader.SetBuffer(kernelDensity, "blockStart", blockStartBuffer);
            SPHComputeShader.SetBuffer(kernelAcceleration, "blockStart", blockStartBuffer);
            SPHComputeShader.SetBuffer(kernelUpdate, "blockStart", blockStartBuffer);
        }
        blockStartBuffer.SetData(blockStart);
        blockIDsBuffer.SetData(blockIDs);
        sortedIDsBuffer.SetData(sortedIDs);
        blockOffsetBuffer.SetData(blockOffset);
        SPHComputeShader.SetInts("blockOffset", blockOffset);
        SPHComputeShader.SetInt("nBlocks", nBlocks);
    }

    void GetBlockOffset(int nx, int ny)
    {
        int n = 0;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                for (int k = -1; k <= 1; k++)
                {
                    blockOffset[n] = i + j * nx + k * nx * ny;
                    n++;
                }
            }
        }
    }

    private void Initialization()
    {
        for (int i = 0; i < particleCount; i++)
        {
            Vector3 pos = new Vector3(Random.Range(0.75f * boundary.minBound.x + 0.25f * boundary.maxBound.x,
                                                   0.25f * boundary.minBound.x + 0.75f * boundary.maxBound.x),
                                      Random.Range(0.75f * boundary.minBound.y + 0.25f * boundary.maxBound.y,
                                                   0.25f * boundary.minBound.y + 0.75f * boundary.maxBound.y),
                                      Random.Range(0.75f * boundary.minBound.z + 0.25f * boundary.maxBound.z,
                                                   0.25f * boundary.minBound.z + 0.75f * boundary.maxBound.z));
            positions[i] = pos;
            velocities[i] = Vector3.zero;
            accelerations[i] = Vector3.zero;
            density[i] = density0;
            mass[i] = particleMass;
            blockIDs[i] = 0;
        }
        BuildBlocks();

        BindingComputeBuffer(kernelDensity);
        BindingComputeBuffer(kernelAcceleration);
        BindingComputeBuffer(kernelUpdate);

        renderMat.SetBuffer("positions", posBuffer);
        renderMat.SetBuffer("velocities", velBuffer);
        renderMat.SetBuffer("densities", denBuffer);
        posBuffer.SetData(positions);
        velBuffer.SetData(velocities);
        accBuffer.SetData(accelerations);
        denBuffer.SetData(density);
        masBuffer.SetData(mass);

        SetCBuffer(); 
    }

    void BindingComputeBuffer(int kernelid)
    {
        SPHComputeShader.SetBuffer(kernelid, "positions", posBuffer);
        SPHComputeShader.SetBuffer(kernelid, "velocities", velBuffer);
        SPHComputeShader.SetBuffer(kernelid, "accelerations", accBuffer);
        SPHComputeShader.SetBuffer(kernelid, "density", denBuffer);
        SPHComputeShader.SetBuffer(kernelid, "mass", masBuffer);
        SPHComputeShader.SetBuffer(kernelid, "blockIDs", blockIDsBuffer);
        SPHComputeShader.SetBuffer(kernelid, "sortedIDs", sortedIDsBuffer);
        SPHComputeShader.SetBuffer(kernelid, "blockOffset", blockOffsetBuffer);
    }
    void SetCBuffer()
    {
        SPHComputeShader.SetFloat("dt", dt);
        SPHComputeShader.SetInt("dispatchX", dispatchX);
        SPHComputeShader.SetInt("particleCount", particleCount);
        SPHComputeShader.SetVector("gravityVector", gravityVector);
        SPHComputeShader.SetFloat("maxSpeed", maxSpeed);
        SPHComputeShader.SetFloat("CollisionDamping", CollisionDamping);
        SPHComputeShader.SetFloat("ShearDamping", ShearDamping);
        SPHComputeShader.SetFloat("smoothLength", smoothLength);
        SPHComputeShader.SetFloat("density0", density0);
        SPHComputeShader.SetFloat("soundSpeed", soundSpeed);
        SPHComputeShader.SetFloat("alpha", alpha);
        SPHComputeShader.SetFloat("beta", beta);
        SPHComputeShader.SetFloat("xmin", boundary.minBound.x + particleSize);
        SPHComputeShader.SetFloat("xmax", boundary.maxBound.x - particleSize);
        SPHComputeShader.SetFloat("ymin", boundary.minBound.y + particleSize);
        SPHComputeShader.SetFloat("ymax", boundary.maxBound.y - particleSize);
        SPHComputeShader.SetFloat("zmin", boundary.minBound.z + particleSize);
        SPHComputeShader.SetFloat("zmax", boundary.maxBound.z - particleSize);
        renderMat.SetFloat("particleSize", particleSize);
        renderMat.SetFloat("soundSpeed", soundSpeed);
    }

    void SafeRelease(ref ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }
    void OnDestroy()
    {
        SafeRelease(ref posBuffer);
        SafeRelease(ref velBuffer);
        SafeRelease(ref accBuffer);
        SafeRelease(ref denBuffer);
        SafeRelease(ref masBuffer);
        SafeRelease(ref blockIDsBuffer);
        SafeRelease(ref sortedIDsBuffer);
        SafeRelease(ref blockStartBuffer);
        SafeRelease(ref blockOffsetBuffer);
        //SafeRelease(ref debugBuffer); 
    }

}

