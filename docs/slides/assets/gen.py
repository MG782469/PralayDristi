"""Builds the slide images from a real PralayDrishti run (Teesta-III) and the validation endpoints."""
import json
import os

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
from matplotlib.colors import LightSource, ListedColormap, LinearSegmentedColormap

HERE = os.path.dirname(os.path.abspath(__file__))
NAVY, GREEN, AMBER, RED = "#1e2a78", "#15803d", "#f59e0b", "#dc2626"


def read_asc(name):
    path = os.path.join(HERE, "asc", name)
    with open(path) as f:
        hdr = {}
        for _ in range(6):
            k, v = f.readline().split()
            hdr[k.lower()] = float(v)
        data = np.loadtxt(f, dtype=np.float32)
    data[data == hdr["nodata_value"]] = np.nan
    return data, hdr


run = json.load(open(os.path.join(HERE, "run.json"), encoding="utf-8"))
dem, hdr = read_asc("dem_used_m.asc")
depth, _ = read_asc("max_depth_m.asc")
haz, _ = read_asc("hazard_class.asc")
arr, _ = read_asc("arrival_min.asc")

west, south, cell = hdr["xllcorner"], hdr["yllcorner"], hdr["cellsize"]
rows, cols = dem.shape
north = south + rows * cell


def to_px(lon, lat):
    return (lon - west) / cell, (north - lat) / cell


# crop to the flooded corridor so the flood is actually visible
wet = np.nan_to_num(depth) > 0.1
rr, cc = np.where(wet)
pad = 10
r0, r1 = max(rr.min() - pad, 0), min(rr.max() + pad, rows)
c0, c1 = max(cc.min() - pad, 0), min(cc.max() + pad, cols)

# zoom window for the flood maps: ~7 km box around the first flooded settlement
_p = None
if _p:
    _x, _y = (_p["lon"] - west) / cell, (north - _p["lat"]) / cell
    zr0, zr1 = int(max(_y - 42, 0)), int(min(_y + 42, rows))
    zc0, zc1 = int(max(_x - 34, 0)), int(min(_x + 34, cols))
else:
    # downstream only: start a little below the dam so the reservoir side is out of the frame
    _dy = (north - run["dam"][1]) / cell
    zr0, zr1, zc0, zc1 = int(min(max(_dy + 25, r0), r1 - 50)), r1, c0, c1

ls = LightSource(azdeg=315, altdeg=40)
shade = ls.hillshade(np.nan_to_num(dem, nan=np.nanmean(dem)), vert_exag=2.0, dx=100, dy=110)


def base(ax, crop=True):
    s = shade[zr0:zr1, zc0:zc1] if crop else shade
    ax.imshow(s, cmap="gray", vmin=0.1, vmax=1.05, interpolation="bilinear")
    ax.set_axis_off()


def save(fig, name):
    fig.savefig(os.path.join(HERE, name), dpi=200, bbox_inches="tight", pad_inches=0.02, transparent=False, facecolor="white")
    plt.close(fig)


def fig_for(ratio_w, ratio_h, scale=2.2):
    return plt.figure(figsize=(ratio_w * scale, ratio_h * scale))


# 1. DEM + traced river + dam (preprocessing)
river = np.array(run["river"])
fig = fig_for(1, (r1 - r0) / (c1 - c0))
ax = fig.add_axes([0, 0, 1, 1])
terrain = LinearSegmentedColormap.from_list("t", ["#1f5f3a", "#6f9a4a", "#c9b27c", "#e8e2d4", "#ffffff"])
d = dem[r0:r1, c0:c1]
ax.imshow(ls.shade(np.nan_to_num(d, nan=np.nanmean(d)), cmap=terrain, blend_mode="soft", vert_exag=2, dx=100, dy=110))
px, py = to_px(river[:, 0], river[:, 1])
ax.plot(px - c0, py - r0, color="#1d4ed8", lw=2.4)
dx, dy = to_px(*run["dam"])
ax.scatter([dx - c0], [dy - r0], s=110, c=AMBER, edgecolors="black", linewidths=1.5, zorder=5)
ax.set_axis_off()
save(fig, "dem_river.png")

# 2. max depth
depth_cmap = LinearSegmentedColormap.from_list("d", ["#c6e9ff", "#6fb6f0", "#2f7fd6", "#1b4fa8", "#2a167a"])
fig = fig_for(1, (zr1 - zr0) / (zc1 - zc0))
ax = fig.add_axes([0, 0, 1, 1])
base(ax)
dd = np.ma.masked_where(np.nan_to_num(depth[zr0:zr1, zc0:zc1]) < 0.1, depth[zr0:zr1, zc0:zc1])
ax.imshow(dd, cmap=depth_cmap, vmin=0, vmax=8, alpha=0.95, interpolation="nearest")
for p in run["impact"]["places"]:
    x, y = to_px(p["lon"], p["lat"])
    ax.scatter([x - zc0], [y - zr0], s=60, c=RED, edgecolors="white", linewidths=1.3, zorder=6)
if zr0 <= dy < zr1:
    ax.scatter([dx - zc0], [dy - zr0], s=110, c=AMBER, edgecolors="black", linewidths=1.5, zorder=6)
save(fig, "flood_depth.png")

# 3. hazard
hz = ListedColormap(["#add8e6", "#ffeda0", "#fec44f", "#fd8d3c", "#e31a1c", "#800026"])
fig = fig_for(1, (zr1 - zr0) / (zc1 - zc0))
ax = fig.add_axes([0, 0, 1, 1])
base(ax)
hh = np.ma.masked_where(np.nan_to_num(haz[zr0:zr1, zc0:zc1]) < 1, haz[zr0:zr1, zc0:zc1])
ax.imshow(hh, cmap=hz, vmin=0.5, vmax=6.5, interpolation="nearest")
for s in run["impact"]["staging"][:4]:
    x, y = to_px(s["lon"], s["lat"])
    if zc0 <= x < zc1 and zr0 <= y < zr1:
        ax.scatter([x - zc0], [y - zr0], s=80, marker="H", c="#16a34a", edgecolors="white", linewidths=1.2, zorder=6)
save(fig, "hazard.png")

# 4. arrival isochrones
fig = fig_for(1, (zr1 - zr0) / (zc1 - zc0))
ax = fig.add_axes([0, 0, 1, 1])
base(ax)
aa = np.ma.masked_where(~(np.nan_to_num(depth[zr0:zr1, zc0:zc1]) > 0.1) | np.isnan(arr[zr0:zr1, zc0:zc1]), arr[zr0:zr1, zc0:zc1])
ax.imshow(aa, cmap="inferno_r", vmin=0, vmax=np.nanpercentile(aa.compressed(), 98), interpolation="nearest")
save(fig, "arrival.png")

# 5. SPH particles (a mid frame of the breach collapse)
nf = run["nearField"]
frames = nf["frames"]
fr = frames[min(len(frames) - 1, int(len(frames) * 0.7))]
fig = plt.figure(figsize=(5.2, 1.9))
ax = fig.add_axes([0, 0, 1, 1])
bx, bz = np.array(nf["bedX"]), np.array(nf["bedZ"])
ax.fill_between(bx, bz, bz.min() - 25, color="#8a6a36")
fx, fz = np.array(fr["x"]), np.array(fr["z"])
ax.scatter(fx, fz, s=2.2, c=fz, cmap="Blues_r", vmin=fz.min() - 20, vmax=fz.max() + 5, linewidths=0)
ax.set_xlim(bx.min(), bx.max())
ax.set_ylim(bz.min() - 20, max(fz.max(), bz.max()) + 10)
ax.set_axis_off()
save(fig, "sph.png")

# 6. breach hydrograph with empirical peaks
h = run["hydrograph"]
fig = plt.figure(figsize=(3.4, 1.6))
ax = fig.add_axes([0.02, 0.05, 0.96, 0.9])
t = np.array(h["t"]) / 60
q = np.array(h["v"])
ax.fill_between(t, q, color="#bfdbfe")
ax.plot(t, q, color="#1d4ed8", lw=2)
ax.set_xlim(0, t.max())
ax.set_ylim(0, q.max() * 1.15)
for s in ["top", "right", "left"]:
    ax.spines[s].set_visible(False)
ax.set_yticks([])
ax.tick_params(labelsize=7, colors="#6b7280")
save(fig, "hydro.png")

# 7. Ritter validation
rt = json.load(open(os.path.join(HERE, "ritter.json")))
fig = plt.figure(figsize=(3.4, 1.5))
ax = fig.add_axes([0.02, 0.05, 0.96, 0.9])
x = np.array(rt["x"])
ax.fill_between(x, rt["model"], color="#bbf7d0")
ax.plot(x, rt["model"], color=GREEN, lw=2.2, label="SWE-2D")
ax.plot(x, rt["exact"], color="black", lw=1.2, ls="--", label="Ritter exact")
ax.legend(frameon=False, fontsize=7, loc="upper right")
for s in ["top", "right", "left"]:
    ax.spines[s].set_visible(False)
ax.set_yticks([])
ax.tick_params(labelsize=7, colors="#6b7280")
save(fig, "ritter.png")

# 8. longitudinal profile
prof = json.load(open(os.path.join(HERE, "profile.json")))
km = np.array([p["km"] for p in prof])
bed = np.array([p["bed"] for p in prof])
ws = np.array([p["ws"] if p["ws"] is not None else np.nan for p in prof])
fig = plt.figure(figsize=(3.4, 1.4))
ax = fig.add_axes([0.02, 0.05, 0.96, 0.9])
ax.fill_between(km, bed, np.where(np.isnan(ws), bed, ws), color="#3b82f6", alpha=0.8)
ax.fill_between(km, bed.min() - 50, bed, color="#8a6a36")
ax.set_xlim(km.min(), km.max())
ax.set_axis_off()
save(fig, "profile.png")

print("crop", r0, r1, c0, c1, "frames", len(frames))
