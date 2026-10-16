import math
import numpy as np


class OneEuroFilter:
    """
    1€ Filter (One Euro Filter) by Casiez, Roussel, and Vogel (CHI 2012).
    Adaptive low-pass filter specifically designed for noisy human motion tracking:
    - At low speeds: low cutoff frequency eliminates micro-jitter.
    - At high speeds: dynamic cutoff frequency increases to eliminate latency.
    """

    def __init__(self, min_cutoff=1.2, beta=0.05, d_cutoff=1.0):
        self.min_cutoff = float(min_cutoff)
        self.beta = float(beta)
        self.d_cutoff = float(d_cutoff)
        self.x_prev = None
        self.dx_prev = None
        self.t_prev = None

    def _smoothing_factor(self, te, cutoff):
        r = 2 * math.pi * cutoff * te
        return r / (r + 1.0)

    def filter(self, x, t):
        x = np.asarray(x, dtype=np.float64)

        if self.t_prev is None:
            self.x_prev = x
            self.dx_prev = np.zeros_like(x)
            self.t_prev = t
            return x

        te = t - self.t_prev
        if te <= 0.0:
            return self.x_prev

        # Estimate derivative
        dx = (x - self.x_prev) / te
        a_d = self._smoothing_factor(te, self.d_cutoff)
        dx_hat = a_d * dx + (1.0 - a_d) * self.dx_prev

        # Dynamic cutoff frequency based on speed
        speed = np.linalg.norm(dx_hat)
        cutoff = self.min_cutoff + self.beta * speed
        a = self._smoothing_factor(te, cutoff)

        x_hat = a * x + (1.0 - a) * self.x_prev

        self.x_prev = x_hat
        self.dx_prev = dx_hat
        self.t_prev = t

        return x_hat

    def reset(self):
        self.x_prev = None
        self.dx_prev = None
        self.t_prev = None


class SkeletonOneEuroFilter:
    """
    Applies independent 1€ Filters across all 33 pose landmarks.
    """

    def __init__(self, min_cutoff=1.5, beta=0.08, d_cutoff=1.0, landmark_count=33):
        # Slightly higher responsiveness (beta) for wrists and knuckles
        self.filters = []
        for i in range(landmark_count):
            if i in (15, 16, 17, 18, 19, 20, 21, 22):  # Hands / Wrists: explosive boxing punches
                self.filters.append(OneEuroFilter(min_cutoff=1.2, beta=0.15, d_cutoff=1.0))
            elif i in (13, 14):  # Elbows
                self.filters.append(OneEuroFilter(min_cutoff=1.5, beta=0.10, d_cutoff=1.0))
            else:  # Head, Shoulders, Torso: ultra-smooth stability
                self.filters.append(OneEuroFilter(min_cutoff=1.0, beta=0.04, d_cutoff=1.0))

    def filter_skeleton(self, landmarks, t):
        result = []
        for i, lm in enumerate(landmarks):
            pos = [lm.x, lm.y, lm.z]
            filtered_pos = self.filters[i].filter(pos, t)
            from data.pose_data import Landmark
            result.append(Landmark(
                x=float(filtered_pos[0]),
                y=float(filtered_pos[1]),
                z=float(filtered_pos[2]),
                visibility=float(lm.visibility)
            ))
        return result

    def reset(self):
        for f in self.filters:
            f.reset()
