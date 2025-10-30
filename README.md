# GPU-Based Smoothed Particle Hydrodynamics (SPH) Simulation in Unity

This project implements a **Smoothed Particle Hydrodynamics (SPH)** solver entirely on the **GPU** using Unity’s **Compute Shaders**.  
It is designed as a lightweight real-time fluid simulation engine capable of running **up to 1 million particles** on a modern GPU (tested on an RTX 4090 Laptop GPU).

---

## Overview

**Smoothed Particle Hydrodynamics (SPH)** is a mesh-free Lagrangian method widely used in computational fluid dynamics and astrophysics.  
Fluids are represented by discrete particles that carry physical quantities (mass, density, velocity, etc.), and interactions between particles are computed through a **smoothing kernel function** to approximate the continuous Navier–Stokes equations.

This implementation:
- Runs all core SPH computations (density, pressure, viscosity, integration) directly on the GPU.
- Uses Unity’s `ComputeShader` API for efficient data-parallel execution.
- Provides a simple visualization system for real-time rendering of particle-based fluids.

---

## Algorithm Details

### Smoothed Particle Hydrodynamics

The density of each particle is computed using a kernel function and the acceleration is derived from pressure and viscosity terms following standard SPH formulations.

For a detailed description of the SPH method, see:

> Price, D. J. *et al.* (2018),  
> *Phantom: A Smoothed Particle Hydrodynamics and Magnetohydrodynamics Code for Astrophysics*,  
> Publications of the Astronomical Society of Australia, **35**, e031.  
> [DOI: 10.1017/pasa.2018.25](https://doi.org/10.1017/pasa.2018.25)

---

## Neighbor Search Optimization

A **grid-based spatial partitioning** algorithm is used for efficient neighbor search:

- The simulation domain is divided into uniform grid cells.  
- Each particle is assigned to a cell based on its spatial position.  
- Only the 27 neighboring cells (including the current one) are searched for potential interactions.

This reduces computational complexity from **O(N²)** to approximately **O(N)**, enabling real-time performance with large particle counts.

---

## Performance

Upto 1,000,000 particles on a RTX 4090 Laptop (125–145W).

---

## Demo Video

YouTuBe: 
Bilibili: https://www.bilibili.com/video/BV1vUsqzrEFk/

---

## Author

Developed by **Shunquan Huang**  
Ph.D. Candidate in Astrophysics, UNLV  

---

## License

This project is released under the **MIT License**.  
Feel free to use, modify, and distribute with attribution.

