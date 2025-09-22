using UnityEngine;
// using static UnityEngine.ParticleSystemForceField;
internal class ParticleSystemForceFieldData
{
    internal static JSONObject GetParticleSystemForceField(UnityEngine.ParticleSystemForceField particleSystemForceField, bool isOverride, NodeMap map, ResoureMap resoureMap)
    {
        JSONObject compData = JsonUtils.SetComponentsType(new JSONObject(JSONObject.Type.OBJECT), "ParticleSystemForceField", isOverride);
        compData.AddField("shape", (int)(object)particleSystemForceField.shape);
        compData.AddField("startRange", particleSystemForceField.startRange);
        compData.AddField("endRange", particleSystemForceField.endRange);
        compData.AddField("directionX", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.directionX));
        compData.AddField("directionY", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.directionY));
        compData.AddField("directionZ", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.directionZ));
        compData.AddField("gravity", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.gravity));
        compData.AddField("gravityFocus", particleSystemForceField.gravityFocus);
        compData.AddField("rotationSpeed", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.rotationSpeed));
        compData.AddField("rotationAttraction", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.rotationAttraction));
        compData.AddField("drag", ParticleSystemData.writeMinMaxCurveData(particleSystemForceField.drag));
        compData.AddField("multiplyDragByParticleSize", particleSystemForceField.multiplyDragByParticleSize);
        compData.AddField("multiplyDragByParticleVelocity", particleSystemForceField.multiplyDragByParticleVelocity);
        return compData;
    }

}