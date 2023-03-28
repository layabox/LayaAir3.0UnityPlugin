using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Util
{

    public interface CustomExport
    {
        void StartHierarchysExport(string savePath);//一次
        bool StartEachHierarchyExport(string hierarchyPath);//N次
        void EndEachHierarchyExport(string hierarchyPath);//N次
        void EndHierarchysExport(string savePath);//一次
    }


}

