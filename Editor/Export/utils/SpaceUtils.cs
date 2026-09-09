using UnityEngine;

public class SpaceUtils
{
    private static Quaternion HelpRotation1 = new Quaternion();
    public static void changePostion(ref Vector3 postion)
    {
        postion.x *= -1;
    }
    public static void changePostion(ref float[] postion)
    {
        postion[0] *= -1;
    }

    /// <summary>
    /// 相机/灯光的直接子节点位置转换：只取反Z（而非X）
    /// 因为父级相机/灯光额外施加了Y轴180°旋转，子节点位置需要补偿。
    /// 数学推导：标准转换(取反X) + Y180补偿 = 只取反Z
    /// </summary>
    public static void changePostionForCameraChild(ref Vector3 postion)
    {
        postion.z *= -1;
    }

    public static void changePostionForCameraChild(ref float[] postion)
    {
        postion[2] *= -1;
    }

    public static void changeRotate(ref Quaternion rotation, bool ischange)
    {
        if (ischange)
        {
            // C R Ry(pi) C, C=diag(-1,1,1). A signed permutation is also
            // valid for derivative tangents and avoids 0 * infinity at steps.
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;
            rotation = new Quaternion(z, w, x, y);
        }
        else
        {
            // 对普通对象：左手系→右手系转换
            rotation.x *= -1;
            rotation.w *= -1;
        }
    }

    public static void changeRotate(ref float[] rotation, bool ischange)
    {
        HelpRotation1.x = rotation[0];
        HelpRotation1.y = rotation[1];
        HelpRotation1.z = rotation[2];
        HelpRotation1.w = rotation[3];
        changeRotate(ref HelpRotation1, ischange);
        rotation[0] = HelpRotation1.x;
        rotation[1] = HelpRotation1.y;
        rotation[2] = HelpRotation1.z;
        rotation[3] = HelpRotation1.w;
    }

    public static void changeRotateTangle(ref float[] rotation, bool ischange = false)
    {
        changeRotate(ref rotation, ischange);
    }

    /// <summary>
    /// 相机/灯光的直接子节点旋转补偿：对标准转换结果左乘Y180
    /// Y180 * (x,y,z,w) = (z, w, -x, -y)
    /// </summary>
    public static void compensateCameraParentRotation(ref Quaternion rotation)
    {
        float ox = rotation.x, oy = rotation.y, oz = rotation.z, ow = rotation.w;
        rotation.x = oz;
        rotation.y = ow;
        rotation.z = -ox;
        rotation.w = -oy;
    }

    public static void compensateCameraParentRotation(ref float[] rotation)
    {
        float ox = rotation[0], oy = rotation[1], oz = rotation[2], ow = rotation[3];
        rotation[0] = oz;
        rotation[1] = ow;
        rotation[2] = -ox;
        rotation[3] = -oy;
    }

    public static void changeRotateEuler(ref float[] eulr, bool ischange)
    {
        // Affine ZXY mapping preserves unwrapped curves and their tangents.
        eulr[0] *= ischange ? -1 : 1;
        eulr[1] = (ischange ? 180 : 0) - eulr[1];
        eulr[2] *= ischange ? 1 : -1;
    }
    public static void changeRotateEulerTangent(ref float[] eulr, bool ischange)
    {
        eulr[0] *= ischange ? -1 : 1;
        eulr[1] *= -1;
        eulr[2] *= ischange ? 1 : -1;
    }

    /// <summary>
    /// 获取左手系到右手系的方向转换向量 (X取反)
    /// CPU粒子导出中用于力/速度等方向量的坐标转换
    /// </summary>
    public static Vector3 getDirection()
    {
        return new Vector3(-1, 1, 1);
    }
}
