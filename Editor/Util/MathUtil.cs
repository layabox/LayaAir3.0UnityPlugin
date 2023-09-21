using System;
using UnityEngine;

namespace Util
{
    class MathUtil
    {
        public static bool Decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
        {
            Matrix4x4 rotationMatrix;
            Decompose(matrix, out scale, out rotationMatrix, out translation);
            RotationMatrix(rotationMatrix, out rotation);
            return true;
        }

        private static bool Decompose(Matrix4x4 matrix, out Vector3 scale, out Matrix4x4 rotation, out Vector3 translation)
        {
            //Get the translation.
            translation.x = matrix.m30;
            translation.y = matrix.m31;
            translation.z = matrix.m32;

            //Scaling is the length of the rows.
            scale.x = (float)Math.Sqrt((matrix.m00 * matrix.m00) + (matrix.m01 * matrix.m01) + (matrix.m02 * matrix.m02));
            scale.y = (float)Math.Sqrt((matrix.m10 * matrix.m10) + (matrix.m11 * matrix.m11) + (matrix.m12 * matrix.m12));
            scale.z = (float)Math.Sqrt((matrix.m20 * matrix.m20) + (matrix.m21 * matrix.m21) + (matrix.m22 * matrix.m22));

            //If any of the scaling factors are zero, than the rotation matrix can not exist.
            if (IsZero(scale.x) || IsZero(scale.y) || IsZero(scale.z))
            {
                rotation = Matrix4x4.identity;
                return false;
            }

            Vector3 at = new Vector3(matrix.m20 / scale.z, matrix.m21 / scale.z, matrix.m22 / scale.z);
            Vector3 up = Vector3.Cross(at, new Vector3(matrix.m00 / scale.x, matrix.m01 / scale.x, matrix.m02 / scale.x));
            Vector3 right = Vector3.Cross(up, at);

            rotation = Matrix4x4.identity;
            rotation.m00 = right.x;
            rotation.m01 = right.y;
            rotation.m02 = right.z;

            rotation.m10 = up.x;
            rotation.m11 = up.y;
            rotation.m12 = up.z;

            rotation.m20 = at.x;
            rotation.m21 = at.y;
            rotation.m22 = at.z;

            // In case of reflexions
            scale.x = Vector3.Dot(right, new Vector3(matrix.m00, matrix.m01, matrix.m02)) > 0.0f ? scale.x : -scale.x;
            scale.y = Vector3.Dot(up, new Vector3(matrix.m10, matrix.m11, matrix.m12)) > 0.0f ? scale.y : -scale.y;
            scale.z = Vector3.Dot(at, new Vector3(matrix.m20, matrix.m21, matrix.m22)) > 0.0f ? scale.z : -scale.z;

            return true;
        }

        private static void RotationMatrix(Matrix4x4 matrix, out Quaternion result)
        {
            float sqrt;
            float half;
            float scale = matrix.m00 + matrix.m11 + matrix.m22;

            if (scale > 0.0f)
            {
                sqrt = (float)Math.Sqrt(scale + 1.0f);
                result.w = sqrt * 0.5f;
                sqrt = 0.5f / sqrt;

                result.x = (matrix.m12 - matrix.m21) * sqrt;
                result.y = (matrix.m20 - matrix.m02) * sqrt;
                result.z = (matrix.m01 - matrix.m10) * sqrt;
            }
            else if ((matrix.m00 >= matrix.m11) && (matrix.m00 >= matrix.m22))
            {
                sqrt = (float)Math.Sqrt(1.0f + matrix.m00 - matrix.m11 - matrix.m22);
                half = 0.5f / sqrt;

                result.x = 0.5f * sqrt;
                result.y = (matrix.m01 + matrix.m10) * half;
                result.z = (matrix.m02 + matrix.m20) * half;
                result.w = (matrix.m12 - matrix.m21) * half;
            }
            else if (matrix.m11 > matrix.m22)
            {
                sqrt = (float)Math.Sqrt(1.0f + matrix.m11 - matrix.m00 - matrix.m22);
                half = 0.5f / sqrt;

                result.x = (matrix.m10 + matrix.m01) * half;
                result.y = 0.5f * sqrt;
                result.z = (matrix.m21 + matrix.m12) * half;
                result.w = (matrix.m20 - matrix.m02) * half;
            }
            else
            {
                sqrt = (float)Math.Sqrt(1.0f + matrix.m22 - matrix.m00 - matrix.m11);
                half = 0.5f / sqrt;

                result.x = (matrix.m20 + matrix.m02) * half;
                result.y = (matrix.m21 + matrix.m12) * half;
                result.z = 0.5f * sqrt;
                result.w = (matrix.m01 - matrix.m10) * half;
            }
        }

        private static bool IsZero(float a)
        {
            return Math.Abs(a) < 1e-6f;
        }

        public static bool isSimilar(float a, float b)
        {
            return a - b <= 0.001;
        }

        public static float Interpolate(float startX, float endX, float start, float end, float tanPoint1, float tanPoint2, float t, out float tangent)
        {
            // Catmull-Rom splines are Hermite curves with special tangent values.
            // Hermite curve formula:
            // (2t^3 - 3t^2 + 1) * p0 + (t^3 - 2t^2 + t) * m0 + (-2t^3 + 3t^2) * p1 + (t^3 - t^2) * m1
            // For points p0 and p1 passing through points m0 and m1 interpolated over t = [0, 1]
            // Tangent M[k] = (P[k+1] - P[k-1]) / 2
            // With [] indicating subscript
            float value = (2.0f * t * t * t - 3.0f * t * t + 1.0f) * start
                + (t * t * t - 2.0f * t * t + t) * tanPoint1
                + (-2.0f * t * t * t + 3.0f * t * t) * end
                + (t * t * t - t * t) * tanPoint2;

            // Calculate tangents
            // p'(t) = (6t² - 6t)p0 + (3t² - 4t + 1)m0 + (-6t² + 6t)p1 + (3t² - 2t)m1
            tangent = (6 * t * t - 6 * t) * start
                + (3 * t * t - 4 * t + 1) * tanPoint1
                + (-6 * t * t + 6 * t) * end
                + (3 * t * t - 2 * t) * tanPoint2;

            tangent /= endX - startX;

            return value;
        }


        public static Matrix4x4 createAffineTransformation(Vector3 trans, Quaternion rot, Vector3 scale) {
            //var oe: Float32Array = out.elements;
           Matrix4x4 matrix = new Matrix4x4();
		    float x = rot.x, y = rot.y, z = rot.z, w = rot.w, x2 = x + x, y2= y + y, z2 = z + z;
            float xx = x* x2, xy = x* y2, xz = x* z2, yy = y* y2, yz = y* z2, zz = z* z2;
            float wx = w* x2, wy = w* y2, wz = w* z2, sx = scale.x, sy = scale.y, sz = scale.z;

            matrix.m00 = (1 - (yy + zz)) * sx;
            matrix.m01 = (xy + wz) * sx;
		    matrix.m02 = (xz - wy) * sx;
            matrix.m03 = 0;
            matrix.m10 = (xy - wz) * sy;
            matrix.m11 = (1 - (xx + zz)) * sy;
            matrix.m12 = (yz + wx) * sy;
            matrix.m13 = 0;
            matrix.m20 = (xz + wy) * sz;
            matrix.m21 = (yz - wx) * sz;
            matrix.m22 = (1 - (xx + yy)) * sz;
            matrix.m23 = 0;
            matrix.m30 = trans.x;
            matrix.m31 = trans.y;
            matrix.m32 = trans.z;
            matrix.m33 = 1;
            return matrix;
	    }

        /**
	 * @internal
	 */
        public static void mulMatrixArray(Matrix4x4 left,float[] right,int rightOffset,out float[] outArray) {
		    float[] l = right;
		    Matrix4x4 r = left;
            float[] e = outArray = new float[16];
            int outOffset = 0;
            outArray = e;

		    float l11 = l[0], l12 = l[1], l13 = l[2], l14 = l[3];
            float l21 = l[4], l22 = l[5], l23 = l[6], l24 = l[7];
            float l31 = l[8], l32 = l[9], l33 = l[10], l34 = l[11];
            float l41 = l[12], l42 = l[13], l43 = l[14], l44 = l[15];

		    float r11 = r.m00,  r12 = r.m01,  r13 = r.m02,  r14 = r.m03;
		    float r21 = r.m10,  r22 = r.m11,  r23 = r.m12,  r24 = r.m13;
		    float r31 = r.m20,  r32 = r.m21,  r33 = r.m22, r34 = r.m23;
		    float r41 = r.m30, r42 = r.m31, r43 = r.m32, r44 = r.m33;

		    e[outOffset] = (l11* r11) + (l12* r21) + (l13* r31) + (l14* r41);
		    e[outOffset + 1] = (l11* r12) + (l12* r22) + (l13* r32) + (l14* r42);
		    e[outOffset + 2] = (l11* r13) + (l12* r23) + (l13* r33) + (l14* r43);
		    e[outOffset + 3] = (l11* r14) + (l12* r24) + (l13* r34) + (l14* r44);
		    e[outOffset + 4] = (l21* r11) + (l22* r21) + (l23* r31) + (l24* r41);
		    e[outOffset + 5] = (l21* r12) + (l22* r22) + (l23* r32) + (l24* r42);
		    e[outOffset + 6] = (l21* r13) + (l22* r23) + (l23* r33) + (l24* r43);
		    e[outOffset + 7] = (l21* r14) + (l22* r24) + (l23* r34) + (l24* r44);
		    e[outOffset + 8] = (l31* r11) + (l32* r21) + (l33* r31) + (l34* r41);
		    e[outOffset + 9] = (l31* r12) + (l32* r22) + (l33* r32) + (l34* r42);
		    e[outOffset + 10] = (l31* r13) + (l32* r23) + (l33* r33) + (l34* r43);
		    e[outOffset + 11] = (l31* r14) + (l32* r24) + (l33* r34) + (l34* r44);
		    e[outOffset + 12] = (l41* r11) + (l42* r21) + (l43* r31) + (l44* r41);
		    e[outOffset + 13] = (l41* r12) + (l42* r22) + (l43* r32) + (l44* r42);
		    e[outOffset + 14] = (l41* r13) + (l42* r23) + (l43* r33) + (l44* r43);
		    e[outOffset + 15] = (l41* r14) + (l42* r24) + (l43* r34) + (l44* r44);
	    }

}
}
