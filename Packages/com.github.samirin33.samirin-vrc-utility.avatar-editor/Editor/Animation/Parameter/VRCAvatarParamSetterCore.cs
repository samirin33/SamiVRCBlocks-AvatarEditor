using UnityEditor;
using UnityEditor.Animations;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCAvatarParamSetterCore
    {
        public static bool HasParameter(AnimatorController controller, string parameterName)
        {
            var parameters = controller.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                    return true;
            }
            return false;
        }
    }
}
