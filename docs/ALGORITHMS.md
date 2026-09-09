# FImageStack: Computational Imaging & Fusion Algorithms

This document provides mathematical formulations, theoretical references, and technical implementation details for the 9 computational photography subsystems in **FImageStack**.

---

## 1. Focus Stacking & Multi-Scale Fusion

Focus stacking reconstructs an all-in-focus image from a sequence of images captured at incrementally varied focal distances.

### 1.1 Focus Measure Metrics
Sharpness at each pixel coordinate $(x, y)$ is evaluated across all frames $k \in [1, N]$:

* **Sum of Modified Laplacian (SML):**
  $$\text{ML}(x, y) = |2I(x, y) - I(x - s, y) - I(x + s, y)| + |2I(x, y) - I(x, y - s) - I(x, y + s)|$$
  $$\text{SML}(x, y) = \sum_{i=-r}^{r} \sum_{j=-r}^{r} \text{ML}(x + i, y + j)$$
  where $s$ is the step size and $r$ is the integration radius.

* **Tenengrad Variance (Sobel Energy):**
  $$G_x = I * S_x, \quad G_y = I * S_y$$
  $$\text{Tenengrad}(x, y) = \sqrt{G_x(x, y)^2 + G_y(x, y)^2}$$

### 1.2 Multi-Scale Laplacian Pyramid Fusion (Burt & Adelson)
1. **Decomposition:** Construct a Gaussian pyramid $G_0, G_1, \dots, G_L$ by successive low-pass filtering and downsampling by $2\times$.
2. **Laplacian Bandpass:** Compute bandpass detail levels $L_l = G_l - \text{Expand}(G_{l+1})$.
3. **Weight Map Generation:** Form weight coefficients $W_{l, k}$ based on local energy metrics.
4. **Pyramid Blending:** At each pyramid level $l$:
   $$L_{\text{fused}, l}(x, y) = \sum_{k=1}^{N} W_{l, k}(x, y) \cdot L_{l, k}(x, y)$$
5. **Reconstruction:** Reconstruct the master image from coarse to fine:
   $$\hat{I}_l = L_{\text{fused}, l} + \text{Expand}(\hat{I}_{l+1})$$

---

## 2. Statistical Noise Stacking

Enhances signal-to-noise ratio (SNR) by combining multiple aligned frames captured in low light or high ISO conditions.

* **Theoretical SNR Boost:**
  $$\text{SNR}_{\text{composite}} = \text{SNR}_{\text{single}} \cdot \sqrt{N}$$
  A 16-frame burst yields up to $+12\text{dB}$ SNR improvement ($4\times$ noise reduction).

* **Kappa-Sigma ($\kappa$-$\sigma$) Clipping:**
  Rejects transient artifacts (satellites, cosmic rays, moving insects) by iteratively discarding outliers beyond $\kappa$ standard deviations from the median:
  $$\text{Keep pixel } I_k(x, y) \iff |I_k(x, y) - \mu(x, y)| \le \kappa \cdot \sigma(x, y)$$

* **Welford $O(1)$ Streaming Accumulator:**
  Calculates online mean $\bar{x}_k$ and sample variance $M_{2, k}$ in a single pass with constant $O(1)$ memory:
  $$\delta = x_k - \bar{x}_{k-1}, \quad \bar{x}_k = \bar{x}_{k-1} + \frac{\delta}{k}, \quad M_{2, k} = M_{2, k-1} + \delta \cdot (x_k - \bar{x}_k)$$

---

## 3. High Dynamic Range (HDR) & Tone Mapping

### 3.1 Mertens Multi-Scale Exposure Fusion
Merges bracketed exposures without requiring camera response curve calibration:
$$W_k(x, y) = \left( C_k(x, y) \right)^{w_c} \cdot \left( S_k(x, y) \right)^{w_s} \cdot \left( E_k(x, y) \right)^{w_e}$$
where:
* **Contrast ($C_k$):** Absolute value of Laplacian filtered luminance.
* **Saturation ($S_k$):** Standard deviation of color channels at pixel $(x, y)$.
* **Well-Exposedness ($E_k$):** Gaussian distance from optimal luminance ($0.5$):
  $$E_k = \exp\left( -\frac{(I - 0.5)^2}{2\sigma^2} \right)$$

### 3.2 ACES Filmic Tone Mapping Curve
Maps high dynamic range linear values $x \in [0, \infty)$ into perceptual display range $[0, 1]$:
$$f(x) = \text{saturate}\left( \frac{x(2.51x + 0.03)}{x(2.43x + 0.59) + 0.14} \right)$$

---

## 4. Astrophotography Deep-Sky Stacking

### 4.1 Star Centroid Detector
Extracts star positions with subpixel precision via 2D Gaussian fitting over local peaks:
$$I(x, y) = A \cdot \exp\left( -\left( \frac{(x - x_0)^2 + (y - y_0)^2}{2\sigma^2} \right) \right) + B$$
Filters non-stellar artifacts using Full Width at Half Maximum (FWHM) and Roundness ($e \ge 0.6$).

### 4.2 Asterism Triangle Matching
Forms invariant geometric triangles among the brightest detected stars:
$$\text{Ratio} = \frac{L_{\text{medium}}}{L_{\text{longest}}}, \quad \text{Angle} = \arccos\left( \frac{\vec{a} \cdot \vec{b}}{\|\vec{a}\| \|\vec{b}\|} \right)$$
Enables rigid registration (translation + rotation) invariant to telescope tracking drift.

### 4.3 Optical Calibration Calibration Frames
$$\text{Calibrated Light} = \frac{\text{Raw Light} - \text{Master Dark}}{\text{Master Flat} - \text{Master Bias}}$$

---

## 5. HST Subpixel Drizzle Super-Resolution

Based on the Hubble Space Telescope (HST) Drizzle algorithm (*Fruchter & Hook 2002*):
* Shrinks input pixels to a smaller synthetic kernel (controlled by the `pixfrac` factor, typically $0.6 - 0.8$).
* Drizzles fractional flux onto a finer subpixel output grid (e.g., $2\times$ or $4\times$ scale).
* Overcomes the spatial Nyquist sampling limit without synthetic halo artifacts.

---

## 6. Optical Image Restoration

### 6.1 Richardson-Lucy Deconvolution
Iteratively recovers the true latent sharp image $f$ from blurry observation $g$ degraded by Point Spread Function (PSF) $h$:
$$f^{(t+1)} = f^{(t)} \cdot \left( \frac{g}{f^{(t)} * h} * h^* \right)$$
* **Total Variation (TV) Damping:** Suppresses high-frequency noise amplification in background regions:
  $$f^{(t+1)} = \frac{f^{(t)}}{1 - \lambda_{\text{TV}} \cdot \text{div}\left(\frac{\nabla f}{|\nabla f|}\right)} \cdot \left( \frac{g}{f^{(t)} * h} * h^* \right)$$

### 6.2 Dark Channel Prior Dehazing
Estimates optical atmospheric haze transmission $t(x)$ using dark channel statistics:
$$J^{\text{dark}}(x) = \min_{c \in \{r, g, b\}} \left( \min_{y \in \Omega(x)} I^c(y) \right)$$
Refines transmission maps via high-speed Guided Filtering.

---

## 7. 3D Depth Reconstruction & Mesh Generation

1. **Continuous Depth Map Generation:** Applies parabolic peak interpolation across the focus stack index:
   $$d^*(x, y) = k^* + \frac{S_{k^*-1} - S_{k^*+1}}{2(S_{k^*-1} - 2S_{k^*} + S_{k^*+1})}$$
2. **Surface Normal Estimation:** Computes gradient vectors using $3\times 3$ Sobel operators:
   $$\vec{N}(x, y) = \text{normalize}\left( -\frac{\partial d}{\partial x}, -\frac{\partial d}{\partial y}, 1 \right)$$
3. **Geometry Exporters:** Generates colorized Point Clouds (`.ply`) and 3D triangular surface meshes (`.obj`) with vertex normal and texture coordinate definitions.

---

## 8. High-Precision Subpixel Image Alignment

Compensates for focus breathing, translation drift, and perspective changes:
* **6-DOF Affine & 8-DOF Homography:** Computes transformation matrices via phase correlation and RANSAC point feature consensus.
* **Elastic Local Mesh Warping:** Deploys a $16\times 16$ deformation grid to absorb non-rigid specimen movement and thermal expansion.

---

## 9. Computational RAW (Bayer Burst Fusion)

* **Merge-before-Demosaic (Google HDR+ Pipeline):**
  Performs multi-frame alignment and accumulation directly on the raw Color Filter Array (CFA) grid prior to demosaicing.
* **Edge-Directed Demosaicing:**
  Interpolates the Green channel along the minimum gradient direction, then derives Red and Blue channels via smooth color difference fields $(R - G)$ and $(B - G)$.
