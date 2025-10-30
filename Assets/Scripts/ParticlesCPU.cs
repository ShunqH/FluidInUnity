using System.Net;
using System.Threading.Tasks;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Tree;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.PlayerSettings;
using static UnityEngine.Mathf;
using static UnityEngine.ParticleSystem;

public class ParticleSimulationCPU : MonoBehaviour
{
    public BoxController boundary;
    public GameObject particlePrefab;
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

    private Vector3[] positions;
    private Vector3[] velocities;
    private Vector3[] accelerations;
    private float[] density;
    private float[] mass;
    private GameObject[] ObjectPts;
    private Vector3 gravityVector;
    private float particleSize;
    private float maxSpeed ;
    private const float PI = 3.1415926535f;
    private int[] blockIDs;
    private int[] sortedIDs;
    private int[] blockStart;
    private int[] counts;
    private int[] blockOffset;
    private int nBlocks; 


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Application.targetFrameRate = 60;
        gravityVector =  new Vector3(0, -gravity, 0);
        maxSpeed = velocityConstraint * soundSpeed;
        particleSize = 0.2f*smoothLength; 
        positions = new Vector3[particleCount];
        velocities = new Vector3[particleCount];
        accelerations = new Vector3[particleCount];
        density = new float[particleCount];
        mass = new float[particleCount];
        blockIDs = new int[particleCount];
        ObjectPts = new GameObject[particleCount];
        blockIDs = new int[particleCount];
        sortedIDs = new int[particleCount];
        blockOffset = new int[27];

        boundary.UpdateBounds();

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
            ObjectPts[i] = Instantiate(particlePrefab, pos, Quaternion.identity);
            ObjectPts[i].transform.position = pos;
            ObjectPts[i].transform.localScale = new Vector3(particleSize, particleSize, particleSize);
        }
    }

    // Update is called once per frame
    void Update()
    {
        buildBlocks(); 
        getDensity();
        getAcceleration();
        updateParticles();
        for (int i = 0; i < particleCount; i++)
        {
            ObjectPts[i].transform.position = positions[i];
            //float normalized = InverseLerp(VisualizationMin, VisualizationMax, density[i]);
            float normalized = InverseLerp(VisualizationMin, VisualizationMax, velocities[i].magnitude);
            Color c = Color.Lerp(ColorMin, ColorMax, normalized);
            ObjectPts[i].GetComponent<Renderer>().material.color = c;
        }
    }

    // Fixed boundary 
    void updateParticles()
    {
        float dt = Time.deltaTime;
        float xmin = boundary.minBound.x + particleSize;
        float xmax = boundary.maxBound.x - particleSize;
        float ymin = boundary.minBound.y + particleSize;
        float ymax = boundary.maxBound.y - particleSize;
        float zmin = boundary.minBound.z + particleSize;
        float zmax = boundary.maxBound.z - particleSize;
        Parallel.For(0, particleCount, i =>
        {
            Vector3 vel = velocities[i] + accelerations[i] * dt;
            float vmag = vel.magnitude;
            if (vmag > maxSpeed)
                vel = vel * maxSpeed / vmag;
            //val *= Lerp(1.0f, Clamp01(maxSpeed / vmag), 0.8f);  //unstable 
            Vector3 pos = positions[i] + vel * dt;

            //BoundaryCheck 
            if (pos.x < xmin) { pos.x = xmin; vel.x *= -1 * CollisionDamping; vel.y *= ShearDamping; vel.z *= ShearDamping; }
            else if (pos.x > xmax) { pos.x = xmax; vel.x *= -1 * CollisionDamping; vel.y *= ShearDamping; vel.z *= ShearDamping; }
            if (pos.y < ymin) { pos.y = ymin; vel.y *= -1 * CollisionDamping; vel.x *= ShearDamping; vel.z *= ShearDamping; }
            else if (pos.y > ymax) { pos.y = ymax; vel.y *= -1 * CollisionDamping; vel.x *= ShearDamping; vel.z *= ShearDamping; }
            if (pos.z < zmin) { pos.z = zmin; vel.z *= -1 * CollisionDamping; vel.x *= ShearDamping; vel.y *= ShearDamping; }
            else if (pos.z > zmax) { pos.z = zmax; vel.z *= -1 * CollisionDamping; vel.x *= ShearDamping; vel.y *= ShearDamping; }
            positions[i] = pos;
            velocities[i] = vel;
        }); 
    }

    // block setup for neighbor search
    void buildBlocks()
    {
        float blockSize = 2f * smoothLength;
        int nx = (int)((boundary.maxBound.x - boundary.minBound.x) / blockSize) + 1;
        int ny = (int)((boundary.maxBound.y - boundary.minBound.y) / blockSize) + 1;
        int nz = (int)((boundary.maxBound.z - boundary.minBound.z) / blockSize) + 1;
        nBlocks = nx * ny * nz;

        getBlockOffset(nx, ny); 
        if (blockStart == null || blockStart.Length < nBlocks + 1)  blockStart = new int[nBlocks + 1];
        else for (int i = 0; i < nBlocks + 1; i++) blockStart[i] = 0;
        if (counts == null || counts.Length < nBlocks) counts = new int[nBlocks];
        else for (int i = 0; i < nBlocks; i++) counts[i] = 0;

        for (int pt = 0; pt < particleCount; pt++) 
        { 
            int i = (int)((positions[pt].x - boundary.minBound.x)/blockSize);
            int j = (int)((positions[pt].y - boundary.minBound.y)/blockSize);
            int k = (int)((positions[pt].z - boundary.minBound.z)/blockSize);
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
    }

    void getBlockOffset(int nx, int ny)
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
 
    // M4 cubic spline kernel (weight function for density)
    float WM4(float q, float h)
    {
        float result = 1f / PI;
        if (q >= 0 && q < 1) { result *= 1f - 1.5f * q * q + 0.75f * q * q * q; }
        else if (q >= 1 && q < 2) { result *= 0.25f * (2f - q) * (2f - q) * (2f - q); }
        else { result = 0f; }
        return result / (h * h * h);
    }
    // gradient for M4 kernel (for pressure/force)
    // dWM4dq = (dW/dq)/smoothLength,
    // dq/dr = (r_ab / |r_ab|) / smoothLength,
    // div(W) = (dW/dq)*(dq/dr) = dWM4dq * (r_ab / |r_ab|)
    float dWM4dq(float dist, float h)
    {
        float result = 1f / PI;
        float q = dist / h;
        if (q >= 0 && q < 1) { result *= -3f * q + 3f * 0.75f * q * q; }
        else if (q >= 1 && q < 2) { result *= -0.75f * (2f - q) * (2f - q); }
        else { result = 0f; }
        return result / (h * h * h * h);
    }

    // density calculations
    void getDensity()
    {
        Parallel.For(0, particleCount, ia =>
        {
            float dens = 0;
            int blockIndex = 0; 
            for (int ioffset = 0; ioffset<27; ioffset++)
            {
                blockIndex = blockIDs[ia] + blockOffset[ioffset];
                if (blockIndex < 0 || blockIndex >= nBlocks) continue; 
                for (int n = blockStart[blockIndex]; n < blockStart[blockIndex+1]; n++)
                {
                    int ib = sortedIDs[n]; 
                    float q = Vector3.Distance(positions[ia], positions[ib]) / smoothLength;
                    dens += mass[ib] * WM4(q, smoothLength);
                }
            }
            density[ia] = dens;
        }); 
    }

    // force and acceleration calculation
    void getAcceleration()
    {
        Parallel.For(0, particleCount, ia =>
        {
            accelerations[ia] = Vector3.zero;
            int blockIndex = 0;
            for (int ioffset = 0; ioffset < 27; ioffset++)
            {
                blockIndex = blockIDs[ia] + blockOffset[ioffset];
                if (blockIndex < 0 || blockIndex >= nBlocks) continue;
                for (int n = blockStart[blockIndex]; n < blockStart[blockIndex + 1]; n++)
                {
                    int ib = sortedIDs[n];
                    if (ia == ib) { continue; }
                    float wh = 0;
                    float coeffViscosity = 0f;
                    float disAB = Vector3.Distance(positions[ia], positions[ib]);
                    float invDisAB = 1.0f / (disAB + 1e-8f);
                    Vector3 vr = positions[ia] - positions[ib];
                    Vector3 vv = velocities[ia] - velocities[ib];
                    float vdotr = Vector3.Dot(vr, vv);
                    vdotr = vdotr * invDisAB;

                    //viscosity terms
                    if (vdotr < 0) { coeffViscosity = -0.5f * (alpha * soundSpeed - beta * vdotr) * vdotr; }
                    wh = dWM4dq(disAB, smoothLength); // div(W) = (dW/dq)*(dq/dr) = wh * (r_ab / |r_ab|)

                    /* Normal Compressable SPH formular */
                    //float term = ((1f / density[ia]) + (1f / density[ib]))*(soundSpeed*soundSpeed + coeffViscosity) * wh;
                    /* End Normal Compressable SPH formular */

                    /* Fix density SPH formular */
                    float term = ((1f / density[ia]) + (1f / density[ib]))
                                - density0 * ((1f / (density[ia] * density[ia])) + (1f / (density[ib] * density[ib])));
                    term *= (soundSpeed * soundSpeed + coeffViscosity) * wh;
                    /* End Fix density SPH formular */

                    term = -mass[ib] * term * invDisAB;
                    accelerations[ia] += term * vr;
                }
            }
            accelerations[ia] += gravityVector;
        }); 
    }
}
