using UnityEngine;

public class SpaceChange
{
    private static readonly Quaternion HelpRotation = new Quaternion(0, 1, 0, 0);
    private static Quaternion HelpRotation1 = new Quaternion();
    private static Vector3 HelpVec3 = new Vector3();
    public static void changePostion(ref Vector3 postion)
    {
        postion.x *= -1;
    }
    public static void changePostion(ref float[] postion)
    {
        postion[0] *= -1;
    }

    public static void changeRotate(ref Quaternion rotation, bool ischange)
    {
        if (ischange)
        {
            rotation *= HelpRotation;
        }
        rotation.x *= -1;
        rotation.w *= -1;
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

    public static void changeRotateTangle(ref float[] rotation)
    {
        rotation[0] *= -1;
        rotation[3] *= -1;
    }

    public static void changeRotateEuler(ref float[] eulr, bool ischange)
    {
        HelpVec3.x = eulr[0];
        HelpVec3.y = eulr[1];
        HelpVec3.z = eulr[2];
        if (ischange)
        {
           
            HelpRotation1.eulerAngles = HelpVec3;
            HelpRotation1 *= HelpRotation;
            Vector3 angles = HelpRotation1.eulerAngles;
            eulr[0] = angles.x;
            eulr[1] = -angles.y;
            eulr[2] = -angles.z;
        }
        else
        {
            eulr[0] = HelpVec3.x;
            eulr[1] = -HelpVec3.y;
            eulr[2] = -HelpVec3.z;
        }
    }
    public static void changeRotateEulerTangent(ref float[] eulr, bool ischange)
    {
        eulr[1] *= -1;
        eulr[2] *= -1;
    }
}
