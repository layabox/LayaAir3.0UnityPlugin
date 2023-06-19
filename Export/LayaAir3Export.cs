using System.Collections.Generic;

public class LayaAir3Export
{
   
    public static void ExportScene()
    {
        GameObjectUitls.init();
        MetarialUitls.init();

        TextureFile.init();
        AnimationCurveGroup.init();
        HierarchyFile hierachy = new HierarchyFile();
        hierachy.saveAllFile(ExportConfig.FirstlevelMenu == 0);
    }
}
