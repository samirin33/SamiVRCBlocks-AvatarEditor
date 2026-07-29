using UnityEditor;
using UnityEngine;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public struct AvatarParamDef
    {
        public string Name;
        public AnimatorControllerParameterType Type;
        public bool DefaultExcluded;
        public string Description;
        public string Range;

        public AvatarParamDef(string name, AnimatorControllerParameterType type, bool excluded, string description = "", string range = "")
        {
            Name = name;
            Type = type;
            DefaultExcluded = excluded;
            Description = description;
            Range = range;
        }
    }
}
