using System.Numerics;

namespace NIFSharp
{
    /// <summary>
    /// A NIF node transform: a translation, a rotation matrix, and a single uniform
    /// scale.
    /// </summary>
    /// <remarks>
    /// NIF has no non-uniform scale, which is why <see cref="Scale"/> is one float
    /// rather than three. Anything non-uniform coming back from FBX has to be baked
    /// into the geometry instead.
    /// </remarks>
    public readonly struct NifTransform(NifVector3 translation, NifMatrix33 rotation, float scale)
    {
        public NifVector3 Translation { get; } = translation;

        public NifMatrix33 Rotation { get; } = rotation;

        public float Scale { get; } = scale;

        public static NifTransform Identity => new(new NifVector3(), NifMatrix33.Identity, 1f);

        /// <summary>Composes this transform with a parent one, parent applied last.</summary>
        public NifTransform ComposedWith(NifTransform parent)
        {
            Matrix4x4 combined = ToMatrix() * parent.ToMatrix();
            return FromMatrix(combined);
        }

        /// <summary>The transform as a row-vector matrix.</summary>
        /// <remarks>
        /// Transposed on the way in. A NIF stores its rotation for column vectors --
        /// it means `R * v`, not `v * R` -- and System.Numerics applies a matrix to a
        /// row vector, so copying the nine numbers across unchanged applies every
        /// rotation backwards.
        ///
        /// It cost a day to see, because a converter that reads and writes with the
        /// same mistake round trips perfectly: the error is only visible against
        /// something that did not make it. Three things say so at once on
        /// lumbermill01waterwheel01. Its axle, `L1_SawWaterWheelHub`, lands at
        /// y[-264, -248] with the rotation applied this way round and at y[-8, 8] --
        /// through the middle of the wheel, where an axle goes -- with it the other.
        /// Its ragdoll rod coincides with the collision capsule that stands for it,
        /// which is two independent paths agreeing. And a skin's own arithmetic closes:
        /// `SkinTransform` composed with where a bone stands has to be one matrix for
        /// the whole skin, and across dlc1sabrecat's fifty-nine bones it disagreed by
        /// 227 units before and by nothing at all after.
        /// </remarks>
        public Matrix4x4 ToMatrix()
        {
            NifMatrix33 r = Rotation;
            float s = Scale;

            return new Matrix4x4(
                r.M11 * s, r.M21 * s, r.M31 * s, 0f,
                r.M12 * s, r.M22 * s, r.M32 * s, 0f,
                r.M13 * s, r.M23 * s, r.M33 * s, 0f,
                Translation.X, Translation.Y, Translation.Z, 1f);
        }

        /// <summary>
        /// Decomposes a matrix back into a NIF transform, taking the scale as the
        /// mean of the three axis lengths.
        /// </summary>
        public static NifTransform FromMatrix(Matrix4x4 m)
        {
            var x = new Vector3(m.M11, m.M12, m.M13);
            var y = new Vector3(m.M21, m.M22, m.M23);
            var z = new Vector3(m.M31, m.M32, m.M33);

            float sx = x.Length();
            float sy = y.Length();
            float sz = z.Length();
            float scale = (sx + sy + sz) / 3f;

            if (sx > 0) x /= sx;
            if (sy > 0) y /= sy;
            if (sz > 0) z /= sz;

            // Transposed back, as ToMatrix transposed on the way in.
            var rotation = new NifMatrix33
            {
                M11 = x.X, M21 = x.Y, M31 = x.Z,
                M12 = y.X, M22 = y.Y, M32 = y.Z,
                M13 = z.X, M23 = z.Y, M33 = z.Z
            };

            return new NifTransform(new NifVector3(m.M41, m.M42, m.M43), rotation, scale);
        }

        /// <summary>Applies the transform to a point.</summary>
        public NifVector3 Apply(NifVector3 point)
        {
            NifMatrix33 r = Rotation;
            float s = Scale;

            return new NifVector3(
                (r.M11 * point.X + r.M12 * point.Y + r.M13 * point.Z) * s + Translation.X,
                (r.M21 * point.X + r.M22 * point.Y + r.M23 * point.Z) * s + Translation.Y,
                (r.M31 * point.X + r.M32 * point.Y + r.M33 * point.Z) * s + Translation.Z);
        }

        /// <summary>Applies only the rotation, for normals and other directions.</summary>
        public NifVector3 ApplyDirection(NifVector3 direction)
        {
            NifMatrix33 r = Rotation;

            return new NifVector3(
                r.M11 * direction.X + r.M12 * direction.Y + r.M13 * direction.Z,
                r.M21 * direction.X + r.M22 * direction.Y + r.M23 * direction.Z,
                r.M31 * direction.X + r.M32 * direction.Y + r.M33 * direction.Z);
        }

        /// <summary>The rotation as a quaternion.</summary>
        public NifQuat ToQuaternion()
        {
            // Read in the form the file stores, as RotationFromQuaternion writes it:
            // the off-diagonal differences are the other way round from the row-vector
            // reading, and taking them the wrong way conjugates every quaternion.
            NifMatrix33 r = Rotation;

            var m = new NifMatrix33
            {
                M11 = r.M11, M12 = r.M21, M13 = r.M31,
                M21 = r.M12, M22 = r.M22, M23 = r.M32,
                M31 = r.M13, M32 = r.M23, M33 = r.M33,
            };

            float trace = m.M11 + m.M22 + m.M33;
            float w, x, y, z;

            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f) * 2f;
                w = 0.25f * s;
                x = (m.M23 - m.M32) / s;
                y = (m.M31 - m.M13) / s;
                z = (m.M12 - m.M21) / s;
            }
            else if (m.M11 > m.M22 && m.M11 > m.M33)
            {
                float s = MathF.Sqrt(1f + m.M11 - m.M22 - m.M33) * 2f;
                w = (m.M23 - m.M32) / s;
                x = 0.25f * s;
                y = (m.M21 + m.M12) / s;
                z = (m.M31 + m.M13) / s;
            }
            else if (m.M22 > m.M33)
            {
                float s = MathF.Sqrt(1f + m.M22 - m.M11 - m.M33) * 2f;
                w = (m.M31 - m.M13) / s;
                x = (m.M21 + m.M12) / s;
                y = 0.25f * s;
                z = (m.M32 + m.M23) / s;
            }
            else
            {
                float s = MathF.Sqrt(1f + m.M33 - m.M11 - m.M22) * 2f;
                w = (m.M12 - m.M21) / s;
                x = (m.M31 + m.M13) / s;
                y = (m.M32 + m.M23) / s;
                z = 0.25f * s;
            }

            return new NifQuat(w, x, y, z);
        }

        /// <summary>
        /// The rotation as Euler angles in **degrees**, in FBX's XYZ order.
        /// </summary>
        /// <remarks>
        /// FBXWrangler goes matrix to quaternion to Euler XYZ (<c>EulOrdXYZs</c>) and
        /// writes degrees into LclRotation. The order is load-bearing: a different
        /// one silently produces wrong rotations for anything but trivial cases.
        /// </remarks>
        public NifVector3 ToEulerDegrees()
        {
            // Transposed into the row form the extraction below is written for; the
            // file stores the column form. See ToMatrix.
            NifMatrix33 r = Rotation;

            var m = new NifMatrix33
            {
                M11 = r.M11, M12 = r.M21, M13 = r.M31,
                M21 = r.M12, M22 = r.M22, M23 = r.M32,
                M31 = r.M13, M32 = r.M23, M33 = r.M33,
            };

            float sy = -m.M13;
            float x, y, z;

            if (sy is > 0.99999f or < -0.99999f)
            {
                // Gimbal lock: yaw is +/-90 degrees and roll folds into yaw.
                y = MathF.CopySign(MathF.PI / 2f, sy);
                x = MathF.Atan2(-m.M32, m.M22);
                z = 0f;
            }
            else
            {
                y = MathF.Asin(sy);
                x = MathF.Atan2(m.M23, m.M33);
                z = MathF.Atan2(m.M12, m.M11);
            }

            const float ToDegrees = 180f / MathF.PI;
            return new NifVector3(x * ToDegrees, y * ToDegrees, z * ToDegrees);
        }

        /// <summary>Builds a rotation matrix from a quaternion.</summary>
        /// <remarks>
        /// The usual column-vector form, which is how a NIF stores a rotation; see
        /// ToMatrix. Getting this backwards mirrors every rotation.
        /// </remarks>
        public static NifMatrix33 RotationFromQuaternion(NifQuat q)
        {
            float x = q.X, y = q.Y, z = q.Z, w = q.W;

            return new NifMatrix33
            {
                M11 = 1f - 2f * (y * y + z * z),
                M21 = 2f * (x * y + z * w),
                M31 = 2f * (x * z - y * w),
                M12 = 2f * (x * y - z * w),
                M22 = 1f - 2f * (x * x + z * z),
                M32 = 2f * (y * z + x * w),
                M13 = 2f * (x * z + y * w),
                M23 = 2f * (y * z - x * w),
                M33 = 1f - 2f * (x * x + y * y)
            };
        }

        /// <summary>Builds a rotation matrix from Euler angles in degrees, XYZ order.</summary>
        public static NifMatrix33 RotationFromEulerDegrees(float x, float y, float z)
        {
            const float ToRadians = MathF.PI / 180f;

            float cx = MathF.Cos(x * ToRadians), sx = MathF.Sin(x * ToRadians);
            float cy = MathF.Cos(y * ToRadians), sy = MathF.Sin(y * ToRadians);
            float cz = MathF.Cos(z * ToRadians), sz = MathF.Sin(z * ToRadians);

            // Built as the row form and stored transposed, which is the form the file
            // keeps. The inverse of ToEulerDegrees.
            return new NifMatrix33
            {
                M11 = cy * cz,
                M21 = cy * sz,
                M31 = -sy,
                M12 = sx * sy * cz - cx * sz,
                M22 = sx * sy * sz + cx * cz,
                M32 = sx * cy,
                M13 = cx * sy * cz + sx * sz,
                M23 = cx * sy * sz - sx * cz,
                M33 = cx * cy
            };
        }

        public override string ToString() =>
            $"T{Translation} S{Scale:G6}";
    }
}
