using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Animations;
using VRC.SDK3.Dynamics.Constraint.Components;
using Samirin33.NDMF.Constraints;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// Constraint系コンポーネントを右クリックしたときに、
    /// UnityのConstraintとVRCConstraintを相互に置き換えるエディタ拡張。
    /// 異なる型間の変換（Position→Parent等）もサポートし、保持できる情報は保持する。
    /// </summary>
    public static class ReplaceConstraint
    {
        #region Menu Items - PositionConstraint

        [MenuItem("CONTEXT/PositionConstraint/Replace with/Unity Rotation Constraint")]
        private static void PositionToUnityRotation(MenuCommand c) => ReplaceTo<PositionConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/Unity Parent Constraint")]
        private static void PositionToUnityParent(MenuCommand c) => ReplaceTo<PositionConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/Unity Scale Constraint")]
        private static void PositionToUnityScale(MenuCommand c) => ReplaceTo<PositionConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/Unity Aim Constraint")]
        private static void PositionToUnityAim(MenuCommand c) => ReplaceTo<PositionConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/Unity LookAt Constraint")]
        private static void PositionToUnityLookAt(MenuCommand c) => ReplaceTo<PositionConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC Position Constraint")]
        private static void PositionToVRCPosition(MenuCommand c) => ReplaceTo<PositionConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC Rotation Constraint")]
        private static void PositionToVRCRotation(MenuCommand c) => ReplaceTo<PositionConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC Parent Constraint")]
        private static void PositionToVRCParent(MenuCommand c) => ReplaceTo<PositionConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC Scale Constraint")]
        private static void PositionToVRCScale(MenuCommand c) => ReplaceTo<PositionConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC Aim Constraint")]
        private static void PositionToVRCAim(MenuCommand c) => ReplaceTo<PositionConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/PositionConstraint/Replace with/VRC LookAt Constraint")]
        private static void PositionToVRCLookAt(MenuCommand c) => ReplaceTo<PositionConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - RotationConstraint

        [MenuItem("CONTEXT/RotationConstraint/Replace with/Unity Position Constraint")]
        private static void RotationToUnityPosition(MenuCommand c) => ReplaceTo<RotationConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/Unity Parent Constraint")]
        private static void RotationToUnityParent(MenuCommand c) => ReplaceTo<RotationConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/Unity Scale Constraint")]
        private static void RotationToUnityScale(MenuCommand c) => ReplaceTo<RotationConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/Unity Aim Constraint")]
        private static void RotationToUnityAim(MenuCommand c) => ReplaceTo<RotationConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/Unity LookAt Constraint")]
        private static void RotationToUnityLookAt(MenuCommand c) => ReplaceTo<RotationConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC Position Constraint")]
        private static void RotationToVRCPosition(MenuCommand c) => ReplaceTo<RotationConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC Rotation Constraint")]
        private static void RotationToVRCRotation(MenuCommand c) => ReplaceTo<RotationConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC Parent Constraint")]
        private static void RotationToVRCParent(MenuCommand c) => ReplaceTo<RotationConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC Scale Constraint")]
        private static void RotationToVRCScale(MenuCommand c) => ReplaceTo<RotationConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC Aim Constraint")]
        private static void RotationToVRCAim(MenuCommand c) => ReplaceTo<RotationConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/RotationConstraint/Replace with/VRC LookAt Constraint")]
        private static void RotationToVRCLookAt(MenuCommand c) => ReplaceTo<RotationConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - ParentConstraint

        [MenuItem("CONTEXT/ParentConstraint/Replace with/Unity Position Constraint")]
        private static void ParentToUnityPosition(MenuCommand c) => ReplaceTo<ParentConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/Unity Rotation Constraint")]
        private static void ParentToUnityRotation(MenuCommand c) => ReplaceTo<ParentConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/Unity Scale Constraint")]
        private static void ParentToUnityScale(MenuCommand c) => ReplaceTo<ParentConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/Unity Aim Constraint")]
        private static void ParentToUnityAim(MenuCommand c) => ReplaceTo<ParentConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/Unity LookAt Constraint")]
        private static void ParentToUnityLookAt(MenuCommand c) => ReplaceTo<ParentConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC Position Constraint")]
        private static void ParentToVRCPosition(MenuCommand c) => ReplaceTo<ParentConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC Rotation Constraint")]
        private static void ParentToVRCRotation(MenuCommand c) => ReplaceTo<ParentConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC Parent Constraint")]
        private static void ParentToVRCParent(MenuCommand c) => ReplaceTo<ParentConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC Scale Constraint")]
        private static void ParentToVRCScale(MenuCommand c) => ReplaceTo<ParentConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC Aim Constraint")]
        private static void ParentToVRCAim(MenuCommand c) => ReplaceTo<ParentConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/ParentConstraint/Replace with/VRC LookAt Constraint")]
        private static void ParentToVRCLookAt(MenuCommand c) => ReplaceTo<ParentConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - ScaleConstraint

        [MenuItem("CONTEXT/ScaleConstraint/Replace with/Unity Position Constraint")]
        private static void ScaleToUnityPosition(MenuCommand c) => ReplaceTo<ScaleConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/Unity Rotation Constraint")]
        private static void ScaleToUnityRotation(MenuCommand c) => ReplaceTo<ScaleConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/Unity Parent Constraint")]
        private static void ScaleToUnityParent(MenuCommand c) => ReplaceTo<ScaleConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/Unity Aim Constraint")]
        private static void ScaleToUnityAim(MenuCommand c) => ReplaceTo<ScaleConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/Unity LookAt Constraint")]
        private static void ScaleToUnityLookAt(MenuCommand c) => ReplaceTo<ScaleConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC Position Constraint")]
        private static void ScaleToVRCPosition(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC Rotation Constraint")]
        private static void ScaleToVRCRotation(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC Parent Constraint")]
        private static void ScaleToVRCParent(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC Scale Constraint")]
        private static void ScaleToVRCScale(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC Aim Constraint")]
        private static void ScaleToVRCAim(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/ScaleConstraint/Replace with/VRC LookAt Constraint")]
        private static void ScaleToVRCLookAt(MenuCommand c) => ReplaceTo<ScaleConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - AimConstraint

        [MenuItem("CONTEXT/AimConstraint/Replace with/Unity Position Constraint")]
        private static void AimToUnityPosition(MenuCommand c) => ReplaceTo<AimConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/Unity Rotation Constraint")]
        private static void AimToUnityRotation(MenuCommand c) => ReplaceTo<AimConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/Unity Parent Constraint")]
        private static void AimToUnityParent(MenuCommand c) => ReplaceTo<AimConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/Unity Scale Constraint")]
        private static void AimToUnityScale(MenuCommand c) => ReplaceTo<AimConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/Unity LookAt Constraint")]
        private static void AimToUnityLookAt(MenuCommand c) => ReplaceTo<AimConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC Position Constraint")]
        private static void AimToVRCPosition(MenuCommand c) => ReplaceTo<AimConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC Rotation Constraint")]
        private static void AimToVRCRotation(MenuCommand c) => ReplaceTo<AimConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC Parent Constraint")]
        private static void AimToVRCParent(MenuCommand c) => ReplaceTo<AimConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC Scale Constraint")]
        private static void AimToVRCScale(MenuCommand c) => ReplaceTo<AimConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC Aim Constraint")]
        private static void AimToVRCAim(MenuCommand c) => ReplaceTo<AimConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/AimConstraint/Replace with/VRC LookAt Constraint")]
        private static void AimToVRCLookAt(MenuCommand c) => ReplaceTo<AimConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - LookAtConstraint

        [MenuItem("CONTEXT/LookAtConstraint/Replace with/Unity Position Constraint")]
        private static void LookAtToUnityPosition(MenuCommand c) => ReplaceTo<LookAtConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/Unity Rotation Constraint")]
        private static void LookAtToUnityRotation(MenuCommand c) => ReplaceTo<LookAtConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/Unity Parent Constraint")]
        private static void LookAtToUnityParent(MenuCommand c) => ReplaceTo<LookAtConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/Unity Scale Constraint")]
        private static void LookAtToUnityScale(MenuCommand c) => ReplaceTo<LookAtConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/Unity Aim Constraint")]
        private static void LookAtToUnityAim(MenuCommand c) => ReplaceTo<LookAtConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC Position Constraint")]
        private static void LookAtToVRCPosition(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC Rotation Constraint")]
        private static void LookAtToVRCRotation(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC Parent Constraint")]
        private static void LookAtToVRCParent(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC Scale Constraint")]
        private static void LookAtToVRCScale(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC Aim Constraint")]
        private static void LookAtToVRCAim(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/LookAtConstraint/Replace with/VRC LookAt Constraint")]
        private static void LookAtToVRCLookAt(MenuCommand c) => ReplaceTo<LookAtConstraint, VRCLookAtConstraint>(c);

        #endregion

        #region Menu Items - VRC Constraints

        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity Position Constraint")]
        private static void VRCPositionToUnityPosition(MenuCommand c) => ReplaceTo<VRCPositionConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCPositionToUnityRotation(MenuCommand c) => ReplaceTo<VRCPositionConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCPositionToUnityParent(MenuCommand c) => ReplaceTo<VRCPositionConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCPositionToUnityScale(MenuCommand c) => ReplaceTo<VRCPositionConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCPositionToUnityAim(MenuCommand c) => ReplaceTo<VRCPositionConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCPositionToUnityLookAt(MenuCommand c) => ReplaceTo<VRCPositionConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/VRC Rotation Constraint")]
        private static void VRCPositionToVRCRotation(MenuCommand c) => ReplaceTo<VRCPositionConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/VRC Parent Constraint")]
        private static void VRCPositionToVRCParent(MenuCommand c) => ReplaceTo<VRCPositionConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/VRC Scale Constraint")]
        private static void VRCPositionToVRCScale(MenuCommand c) => ReplaceTo<VRCPositionConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/VRC Aim Constraint")]
        private static void VRCPositionToVRCAim(MenuCommand c) => ReplaceTo<VRCPositionConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/VRCPositionConstraint/Replace with/VRC LookAt Constraint")]
        private static void VRCPositionToVRCLookAt(MenuCommand c) => ReplaceTo<VRCPositionConstraint, VRCLookAtConstraint>(c);

        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity Position Constraint")]
        private static void VRCRotationToUnityPosition(MenuCommand c) => ReplaceTo<VRCRotationConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCRotationToUnityRotation(MenuCommand c) => ReplaceTo<VRCRotationConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCRotationToUnityParent(MenuCommand c) => ReplaceTo<VRCRotationConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCRotationToUnityScale(MenuCommand c) => ReplaceTo<VRCRotationConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCRotationToUnityAim(MenuCommand c) => ReplaceTo<VRCRotationConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCRotationToUnityLookAt(MenuCommand c) => ReplaceTo<VRCRotationConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/VRC Position Constraint")]
        private static void VRCRotationToVRCPosition(MenuCommand c) => ReplaceTo<VRCRotationConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/VRC Parent Constraint")]
        private static void VRCRotationToVRCParent(MenuCommand c) => ReplaceTo<VRCRotationConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/VRC Scale Constraint")]
        private static void VRCRotationToVRCScale(MenuCommand c) => ReplaceTo<VRCRotationConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/VRC Aim Constraint")]
        private static void VRCRotationToVRCAim(MenuCommand c) => ReplaceTo<VRCRotationConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/VRCRotationConstraint/Replace with/VRC LookAt Constraint")]
        private static void VRCRotationToVRCLookAt(MenuCommand c) => ReplaceTo<VRCRotationConstraint, VRCLookAtConstraint>(c);

        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity Position Constraint")]
        private static void VRCParentToUnityPosition(MenuCommand c) => ReplaceTo<VRCParentConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCParentToUnityRotation(MenuCommand c) => ReplaceTo<VRCParentConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCParentToUnityParent(MenuCommand c) => ReplaceTo<VRCParentConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCParentToUnityScale(MenuCommand c) => ReplaceTo<VRCParentConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCParentToUnityAim(MenuCommand c) => ReplaceTo<VRCParentConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCParentToUnityLookAt(MenuCommand c) => ReplaceTo<VRCParentConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/VRC Position Constraint")]
        private static void VRCParentToVRCPosition(MenuCommand c) => ReplaceTo<VRCParentConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/VRC Rotation Constraint")]
        private static void VRCParentToVRCRotation(MenuCommand c) => ReplaceTo<VRCParentConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/VRC Scale Constraint")]
        private static void VRCParentToVRCScale(MenuCommand c) => ReplaceTo<VRCParentConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/VRC Aim Constraint")]
        private static void VRCParentToVRCAim(MenuCommand c) => ReplaceTo<VRCParentConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/VRCParentConstraint/Replace with/VRC LookAt Constraint")]
        private static void VRCParentToVRCLookAt(MenuCommand c) => ReplaceTo<VRCParentConstraint, VRCLookAtConstraint>(c);

        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity Position Constraint")]
        private static void VRCScaleToUnityPosition(MenuCommand c) => ReplaceTo<VRCScaleConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCScaleToUnityRotation(MenuCommand c) => ReplaceTo<VRCScaleConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCScaleToUnityParent(MenuCommand c) => ReplaceTo<VRCScaleConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCScaleToUnityScale(MenuCommand c) => ReplaceTo<VRCScaleConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCScaleToUnityAim(MenuCommand c) => ReplaceTo<VRCScaleConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCScaleToUnityLookAt(MenuCommand c) => ReplaceTo<VRCScaleConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/VRC Position Constraint")]
        private static void VRCScaleToVRCPosition(MenuCommand c) => ReplaceTo<VRCScaleConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/VRC Rotation Constraint")]
        private static void VRCScaleToVRCRotation(MenuCommand c) => ReplaceTo<VRCScaleConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/VRC Parent Constraint")]
        private static void VRCScaleToVRCParent(MenuCommand c) => ReplaceTo<VRCScaleConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/VRC Aim Constraint")]
        private static void VRCScaleToVRCAim(MenuCommand c) => ReplaceTo<VRCScaleConstraint, VRCAimConstraint>(c);
        [MenuItem("CONTEXT/VRCScaleConstraint/Replace with/VRC LookAt Constraint")]
        private static void VRCScaleToVRCLookAt(MenuCommand c) => ReplaceTo<VRCScaleConstraint, VRCLookAtConstraint>(c);

        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity Position Constraint")]
        private static void VRCAimToUnityPosition(MenuCommand c) => ReplaceTo<VRCAimConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCAimToUnityRotation(MenuCommand c) => ReplaceTo<VRCAimConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCAimToUnityParent(MenuCommand c) => ReplaceTo<VRCAimConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCAimToUnityScale(MenuCommand c) => ReplaceTo<VRCAimConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCAimToUnityAim(MenuCommand c) => ReplaceTo<VRCAimConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCAimToUnityLookAt(MenuCommand c) => ReplaceTo<VRCAimConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/VRC Position Constraint")]
        private static void VRCAimToVRCPosition(MenuCommand c) => ReplaceTo<VRCAimConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/VRC Rotation Constraint")]
        private static void VRCAimToVRCRotation(MenuCommand c) => ReplaceTo<VRCAimConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/VRC Parent Constraint")]
        private static void VRCAimToVRCParent(MenuCommand c) => ReplaceTo<VRCAimConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/VRC Scale Constraint")]
        private static void VRCAimToVRCScale(MenuCommand c) => ReplaceTo<VRCAimConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCAimConstraint/Replace with/VRC LookAt Constraint")]
        private static void VRCAimToVRCLookAt(MenuCommand c) => ReplaceTo<VRCAimConstraint, VRCLookAtConstraint>(c);

        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity Position Constraint")]
        private static void VRCLookAtToUnityPosition(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, PositionConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity Rotation Constraint")]
        private static void VRCLookAtToUnityRotation(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, RotationConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity Parent Constraint")]
        private static void VRCLookAtToUnityParent(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, ParentConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity Scale Constraint")]
        private static void VRCLookAtToUnityScale(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, ScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity Aim Constraint")]
        private static void VRCLookAtToUnityAim(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, AimConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/Unity LookAt Constraint")]
        private static void VRCLookAtToUnityLookAt(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, LookAtConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/VRC Position Constraint")]
        private static void VRCLookAtToVRCPosition(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, VRCPositionConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/VRC Rotation Constraint")]
        private static void VRCLookAtToVRCRotation(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, VRCRotationConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/VRC Parent Constraint")]
        private static void VRCLookAtToVRCParent(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, VRCParentConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/VRC Scale Constraint")]
        private static void VRCLookAtToVRCScale(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, VRCScaleConstraint>(c);
        [MenuItem("CONTEXT/VRCLookAtConstraint/Replace with/VRC Aim Constraint")]
        private static void VRCLookAtToVRCAim(MenuCommand c) => ReplaceTo<VRCLookAtConstraint, VRCAimConstraint>(c);

        #endregion

        #region Core Logic

        private static void ReplaceTo<TSource, TTarget>(MenuCommand command)
            where TSource : Component
            where TTarget : Component
        {
            var sourceComponent = command.context as TSource;
            if (sourceComponent == null) return;

            var gameObject = sourceComponent.gameObject;
            var sourceWrapper = AllConstraint.FromComponent(sourceComponent);
            if (sourceWrapper == null)
            {
                Debug.LogWarning("[ReplaceConstraint] 対応していないConstraint型です。");
                return;
            }

            Undo.RecordObject(gameObject, "Replace Constraint");

            // 置換前にコンポーネントのアタッチ順序インデックスを取得
            var sourceComponentIndex = GetComponentIndex(gameObject, sourceComponent);

            var targetComponent = Undo.AddComponent<TTarget>(gameObject);
            Undo.RecordObject(targetComponent, "Replace Constraint");

            // 共通プロパティをコピー
            var targetWrapper = AllConstraint.FromComponent(targetComponent);
            if (targetWrapper != null)
            {
                targetWrapper.Weight = sourceWrapper.Weight;
                targetWrapper.IsActive = sourceWrapper.IsActive;

                var sourceCount = sourceWrapper.SourceCount;
                for (int i = 0; i < sourceCount; i++)
                {
                    var t = sourceWrapper.GetSource(i);
                    var w = GetSourceWeight(sourceComponent, sourceWrapper, i);
                    // Sourceがnull(None)の場合もWeight等の情報を保持するため追加する
                    targetWrapper.AddSource(t, w);
                }
            }

            // 型固有のプロパティをコピー（保持できる情報は保持）
            CopyTypeSpecific(sourceComponent, targetComponent);

            // VRCConstraint特有のプロパティをコピー（SolveInLocalSpace, TargetTransform等）
            CopyVRCCommonProperties(sourceComponent, targetComponent);

            // VRC Constraintの場合は変更を適用
            ApplyVRCModifiedChanges(targetComponent);

            // 親階層のAnimatorに含まれるClipで置換元Constraintを制御していたバインディングを置換先にリバインド（設定で有効な場合）
            if (ReplaceConstraintPreferences.AutoFixAnimatorPath)
                RebindAnimationClipsInParentAnimator(sourceComponent, targetComponent);

            Undo.DestroyObjectImmediate(sourceComponent);

            // コンポーネントのアタッチ順序を元のインデックスに復元
            if (sourceComponentIndex >= 0)
            {
                Undo.RecordObject(gameObject, "Replace Constraint");
                MoveComponentToIndex(targetComponent, sourceComponentIndex);
            }
        }

        /// <summary>
        /// GameObject上のコンポーネントのインデックス（Inspector表示順）を取得する。
        /// </summary>
        private static int GetComponentIndex(GameObject go, Component component)
        {
            var components = go.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == component) return i;
            }
            return -1;
        }

        /// <summary>
        /// コンポーネントを指定インデックス位置に移動する。
        /// AddComponentは末尾に追加されるため、Replace後に元の順序を復元するために使用。
        /// </summary>
        private static void MoveComponentToIndex(Component component, int targetIndex)
        {
            if (component == null || targetIndex < 0) return;

            var go = component.gameObject;
            var components = go.GetComponents<Component>();
            var currentIndex = -1;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] == component) { currentIndex = i; break; }
            }
            if (currentIndex < 0 || currentIndex <= targetIndex) return;

            var movesUp = currentIndex - targetIndex;
            for (int i = 0; i < movesUp; i++)
            {
                if (!ComponentUtility.MoveComponentUp(component)) break;
            }
        }

        private static float GetSourceWeight(Component constraint, AllConstraint wrapper, int index)
        {
            if (wrapper.IsVRC) return wrapper.AsVRC.GetSourceWeight(index);
            if (constraint is PositionConstraint pos) return pos.GetSource(index).weight;
            if (constraint is RotationConstraint rot) return rot.GetSource(index).weight;
            if (constraint is ParentConstraint parent) return parent.GetSource(index).weight;
            if (constraint is ScaleConstraint scale) return scale.GetSource(index).weight;
            if (constraint is AimConstraint aim) return aim.GetSource(index).weight;
            if (constraint is LookAtConstraint lookAt) return lookAt.GetSource(index).weight;
            return 1f;
        }

        /// <summary>
        /// VRCConstraintのAdvanced Settings（SolveInLocalSpace, TargetTransform, FreezeToWorld等）をコピーする。
        /// SerializedObjectを使用してUnityのシリアライズシステムに正しく反映する。
        /// </summary>
        private static void CopyVRCCommonProperties(Component src, Component dst)
        {
            if (src == null || dst == null) return;
            var srcType = src.GetType();
            var dstType = dst.GetType();
            if (srcType.Namespace == null || !srcType.Namespace.StartsWith("VRC.") ||
                dstType.Namespace == null || !dstType.Namespace.StartsWith("VRC."))
                return;

            var advancedSettingNames = new[] { "SolveInLocalSpace", "TargetTransform", "FreezeToWorld", "RebakeOffsetsWhenUnfrozen", "Locked" };

            using (var srcSO = new SerializedObject(src))
            using (var dstSO = new SerializedObject(dst))
            {
                foreach (var propName in advancedSettingNames)
                {
                    var srcProp = srcSO.FindProperty(propName);
                    var dstProp = dstSO.FindProperty(propName);
                    if (srcProp != null && dstProp != null && srcProp.propertyType == dstProp.propertyType)
                    {
                        CopySerializedProperty(srcProp, dstProp);
                    }
                }
                dstSO.ApplyModifiedProperties();
            }
        }

        /// <summary>
        /// SerializedPropertyの値をコピーする。
        /// </summary>
        private static void CopySerializedProperty(SerializedProperty src, SerializedProperty dst)
        {
            switch (src.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    dst.boolValue = src.boolValue;
                    break;
                case SerializedPropertyType.Float:
                    dst.floatValue = src.floatValue;
                    break;
                case SerializedPropertyType.Integer:
                    dst.intValue = src.intValue;
                    break;
                case SerializedPropertyType.ObjectReference:
                    dst.objectReferenceValue = src.objectReferenceValue;
                    break;
                case SerializedPropertyType.Enum:
                    dst.enumValueIndex = src.enumValueIndex;
                    break;
                case SerializedPropertyType.Vector3:
                    dst.vector3Value = src.vector3Value;
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// VRC Constraintの場合はApplyModifiedChangesを呼び出して変更を適用する。
        /// </summary>
        private static void ApplyVRCModifiedChanges(Component target)
        {
            if (target == null) return;
            var type = target.GetType();
            if (type.Namespace == null || !type.Namespace.StartsWith("VRC.")) return;

            try
            {
                var method = type.GetMethod("ApplyModifiedChanges", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                method?.Invoke(target, null);
            }
            catch { /* メソッドが存在しない場合は無視 */ }
        }

        /// <summary>
        /// 親階層のAnimatorに含まれるAnimationClipで、置換元Constraintを制御していたバインディングを置換先にリバインドする。
        /// </summary>
        private static void RebindAnimationClipsInParentAnimator(Component sourceComponent, Component targetComponent)
        {
            if (sourceComponent == null || targetComponent == null) return;

            var animator = FindAnimatorInParents(sourceComponent.transform);
            if (animator == null) return;

            var controller = animator.runtimeAnimatorController;
            if (controller == null) return;

            var clips = GetAllAnimationClips(controller);
            if (clips == null || clips.Count == 0) return;

            var constraintPath = AnimationUtility.CalculateTransformPath(sourceComponent.transform, animator.transform);
            var sourceType = sourceComponent.GetType();
            var targetType = targetComponent.GetType();

            int totalRebound = 0;
            var clipsToModify = clips.Where(c => c != null).ToList();
            if (clipsToModify.Count > 0)
            {
                Undo.RegisterCompleteObjectUndo(clipsToModify.ToArray(), "Replace Constraint (Animation Rebind)");
            }
            foreach (var clip in clipsToModify)
            {
                totalRebound += RebindConstraintInClip(clip, constraintPath, sourceType, targetType);
            }

            if (totalRebound > 0)
            {
                foreach (var clip in clipsToModify)
                {
                    EditorUtility.SetDirty(clip);
                }
                AssetDatabase.SaveAssets();
            }
        }

        /// <summary>
        /// 指定Transformの親階層からAnimatorを検索する。
        /// </summary>
        private static Animator FindAnimatorInParents(Transform transform)
        {
            var current = transform;
            while (current != null)
            {
                var animator = current.GetComponent<Animator>();
                if (animator != null) return animator;
                current = current.parent;
            }
            return null;
        }

        /// <summary>
        /// RuntimeAnimatorControllerから全AnimationClipを取得する（AnimatorOverrideControllerのオーバーライド含む）。
        /// </summary>
        private static List<AnimationClip> GetAllAnimationClips(RuntimeAnimatorController controller)
        {
            var clips = new HashSet<AnimationClip>();

            if (controller is AnimatorOverrideController overrideController)
            {
                var baseController = overrideController.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (baseController != null)
                {
                    var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
                    overrideController.GetOverrides(overrides);
                    foreach (var kv in overrides)
                    {
                        clips.Add(kv.Value != null ? kv.Value : kv.Key);
                    }
                }
            }
            else if (controller is UnityEditor.Animations.AnimatorController animController)
            {
                foreach (var clip in animController.animationClips)
                    clips.Add(clip);
            }

            return clips.ToList();
        }

        /// <summary>
        /// 1つのAnimationClip内で、指定パス・置換元型のバインディングを置換先型にリバインドする。
        /// </summary>
        private static int RebindConstraintInClip(AnimationClip clip, string constraintPath, Type sourceType, Type targetType)
        {
            int count = 0;

            var curveBindings = AnimationUtility.GetCurveBindings(clip);
            foreach (var binding in curveBindings)
            {
                if (binding.path != constraintPath || binding.type != sourceType) continue;

                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                AnimationUtility.SetEditorCurve(clip, binding, null);

                var newPropName = MapConstraintPropertyName(binding.propertyName, sourceType, targetType);
                var newBinding = new EditorCurveBinding
                {
                    path = binding.path,
                    propertyName = newPropName,
                    type = targetType
                };
                AnimationUtility.SetEditorCurve(clip, newBinding, curve);
                count++;
            }

            var objBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (var binding in objBindings)
            {
                if (binding.path != constraintPath || binding.type != sourceType) continue;

                var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);

                var newPropName = MapConstraintPropertyName(binding.propertyName, sourceType, targetType);
                var newBinding = new EditorCurveBinding
                {
                    path = binding.path,
                    propertyName = newPropName,
                    type = targetType
                };
                AnimationUtility.SetObjectReferenceCurve(clip, newBinding, keyframes);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Unity ConstraintとVRC Constraint間のプロパティ名マッピング。
        /// VRC: Sources.source0.Weight, Sources.source0.SourceTransform 等
        /// Unity: m_Sources.Array.data[0].weight, m_Sources.Array.data[0].sourceTransform 等
        /// </summary>
        private static string MapConstraintPropertyName(string propertyName, Type sourceType, Type targetType)
        {
            if (string.IsNullOrEmpty(propertyName)) return propertyName;
            if (sourceType == targetType) return propertyName;

            var srcIsVRC = sourceType.Namespace?.StartsWith("VRC.") == true;
            var dstIsVRC = targetType.Namespace?.StartsWith("VRC.") == true;

            if (srcIsVRC && !dstIsVRC)
            {
                return MapPropertyNameVRCToUnity(propertyName);
            }
            if (!srcIsVRC && dstIsVRC)
            {
                return MapPropertyNameUnityToVRC(propertyName);
            }
            return propertyName;
        }

        /// <summary>
        /// VRC形式のプロパティ名をUnity形式に変換する。
        /// IsActive, Freeze X/Y/Z, At Rest, Offset 等の表記の違いをマッピング。
        /// </summary>
        private static string MapPropertyNameVRCToUnity(string propertyName)
        {
            var m = Regex.Match(propertyName, @"^Sources\.source(\d+)\.(Weight|SourceTransform)$");
            if (m.Success)
                return $"m_Sources.Array.data[{m.Groups[1].Value}].{m.Groups[2].Value.ToLowerInvariant()}";

            return propertyName switch
            {
                "GlobalWeight" => "m_Weight",
                "IsActive" => "m_Active",
                "Locked" => "m_IsLocked",
                "AffectsPositionX" => "m_AffectTranslationX",
                "AffectsPositionY" => "m_AffectTranslationY",
                "AffectsPositionZ" => "m_AffectTranslationZ",
                "AffectsRotationX" => "m_AffectRotationX",
                "AffectsRotationY" => "m_AffectRotationY",
                "AffectsRotationZ" => "m_AffectRotationZ",
                "AffectsScaleX" => "m_AffectScalingX",
                "AffectsScaleY" => "m_AffectScalingY",
                "AffectsScaleZ" => "m_AffectScalingZ",
                "PositionAtRest.x" => "m_TranslationAtRest.x",
                "PositionAtRest.y" => "m_TranslationAtRest.y",
                "PositionAtRest.z" => "m_TranslationAtRest.z",
                "PositionOffset.x" => "m_TranslationOffset.x",
                "PositionOffset.y" => "m_TranslationOffset.y",
                "PositionOffset.z" => "m_TranslationOffset.z",
                "RotationAtRest.x" => "m_RotationAtRest.x",
                "RotationAtRest.y" => "m_RotationAtRest.y",
                "RotationAtRest.z" => "m_RotationAtRest.z",
                "RotationOffset.x" => "m_RotationOffset.x",
                "RotationOffset.y" => "m_RotationOffset.y",
                "RotationOffset.z" => "m_RotationOffset.z",
                "ScaleAtRest.x" => "m_ScaleAtRest.x",
                "ScaleAtRest.y" => "m_ScaleAtRest.y",
                "ScaleAtRest.z" => "m_ScaleAtRest.z",
                "ScaleOffset.x" => "m_ScaleOffset.x",
                "ScaleOffset.y" => "m_ScaleOffset.y",
                "ScaleOffset.z" => "m_ScaleOffset.z",
                _ => propertyName
            };
        }

        /// <summary>
        /// Unity形式のプロパティ名をVRC形式に変換する。
        /// </summary>
        private static string MapPropertyNameUnityToVRC(string propertyName)
        {
            var m = Regex.Match(propertyName, @"^m_Sources\.Array\.data\[(\d+)\]\.(weight|sourceTransform)$");
            if (m.Success)
            {
                var prop = m.Groups[2].Value;
                var propName = prop == "weight" ? "Weight" : "SourceTransform";
                return $"Sources.source{m.Groups[1].Value}.{propName}";
            }

            return propertyName switch
            {
                "m_Weight" => "GlobalWeight",
                "m_Active" => "IsActive",
                "m_Locked" => "Locked",
                "m_AffectTranslationX" => "AffectsPositionX",
                "m_AffectTranslationY" => "AffectsPositionY",
                "m_AffectTranslationZ" => "AffectsPositionZ",
                "m_AffectRotationX" => "AffectsRotationX",
                "m_AffectRotationY" => "AffectsRotationY",
                "m_AffectRotationZ" => "AffectsRotationZ",
                "m_AffectScalingX" => "AffectsScaleX",
                "m_AffectScalingY" => "AffectsScaleY",
                "m_AffectScalingZ" => "AffectsScaleZ",
                "m_TranslationAtRest.x" => "PositionAtRest.x",
                "m_TranslationAtRest.y" => "PositionAtRest.y",
                "m_TranslationAtRest.z" => "PositionAtRest.z",
                "m_TranslationOffset.x" => "PositionOffset.x",
                "m_TranslationOffset.y" => "PositionOffset.y",
                "m_TranslationOffset.z" => "PositionOffset.z",
                "m_RotationAtRest.x" => "RotationAtRest.x",
                "m_RotationAtRest.y" => "RotationAtRest.y",
                "m_RotationAtRest.z" => "RotationAtRest.z",
                "m_RotationOffset.x" => "RotationOffset.x",
                "m_RotationOffset.y" => "RotationOffset.y",
                "m_RotationOffset.z" => "RotationOffset.z",
                "m_ScaleAtRest.x" => "ScaleAtRest.x",
                "m_ScaleAtRest.y" => "ScaleAtRest.y",
                "m_ScaleAtRest.z" => "ScaleAtRest.z",
                "m_ScaleOffset.x" => "ScaleOffset.x",
                "m_ScaleOffset.y" => "ScaleOffset.y",
                "m_ScaleOffset.z" => "ScaleOffset.z",
                _ => propertyName
            };
        }

        /// <summary>
        /// ソースからターゲットへ型固有プロパティをコピー。保持できる情報は可能な限り保持する。
        /// </summary>
        private static void CopyTypeSpecific(Component src, Component dst)
        {
            // Position系 → 各種
            if (src is PositionConstraint posSrc)
            {
                if (dst is VRCPositionConstraint vrcPos) CopyPositionToVRCPosition(posSrc, vrcPos);
                else if (dst is ParentConstraint parent) CopyPositionToParent(posSrc, parent);
                else if (dst is VRCParentConstraint vrcParent) CopyPositionToVRCParent(posSrc, vrcParent);
            }
            else if (src is VRCPositionConstraint vrcPosSrc)
            {
                if (dst is PositionConstraint pos) CopyVRCPositionToPosition(vrcPosSrc, pos);
                else if (dst is ParentConstraint parent) CopyVRCPositionToParent(vrcPosSrc, parent);
                else if (dst is VRCParentConstraint vrcParent) CopyVRCPositionToVRCParent(vrcPosSrc, vrcParent);
            }

            // Rotation系 → 各種
            else if (src is RotationConstraint rotSrc)
            {
                if (dst is VRCRotationConstraint vrcRot) CopyRotationToVRCRotation(rotSrc, vrcRot);
                else if (dst is ParentConstraint parent) CopyRotationToParent(rotSrc, parent);
                else if (dst is VRCParentConstraint vrcParent) CopyRotationToVRCParent(rotSrc, vrcParent);
            }
            else if (src is VRCRotationConstraint vrcRotSrc)
            {
                if (dst is RotationConstraint rot) CopyVRCRotationToRotation(vrcRotSrc, rot);
                else if (dst is ParentConstraint parent) CopyVRCRotationToParent(vrcRotSrc, parent);
                else if (dst is VRCParentConstraint vrcParent) CopyVRCRotationToVRCParent(vrcRotSrc, vrcParent);
            }

            // Parent系 → 各種
            else if (src is ParentConstraint parentSrc)
            {
                if (dst is VRCParentConstraint vrcParent) CopyParentToVRCParent(parentSrc, vrcParent);
                else if (dst is PositionConstraint pos) CopyParentToPosition(parentSrc, pos);
                else if (dst is VRCPositionConstraint vrcPos) CopyParentToVRCPosition(parentSrc, vrcPos);
                else if (dst is RotationConstraint rot) CopyParentToRotation(parentSrc, rot);
                else if (dst is VRCRotationConstraint vrcRot) CopyParentToVRCRotation(parentSrc, vrcRot);
            }
            else if (src is VRCParentConstraint vrcParentSrc)
            {
                if (dst is ParentConstraint parent) CopyVRCParentToParent(vrcParentSrc, parent);
                else if (dst is PositionConstraint pos) CopyVRCParentToPosition(vrcParentSrc, pos);
                else if (dst is VRCPositionConstraint vrcPos) CopyVRCParentToVRCPosition(vrcParentSrc, vrcPos);
                else if (dst is RotationConstraint rot) CopyVRCParentToRotation(vrcParentSrc, rot);
                else if (dst is VRCRotationConstraint vrcRot) CopyVRCParentToVRCRotation(vrcParentSrc, vrcRot);
            }

            // Scale系 → 各種
            else if (src is ScaleConstraint scaleSrc)
            {
                if (dst is VRCScaleConstraint vrcScale) CopyScaleToVRCScale(scaleSrc, vrcScale);
            }
            else if (src is VRCScaleConstraint vrcScaleSrc)
            {
                if (dst is ScaleConstraint scale) CopyVRCScaleToScale(vrcScaleSrc, scale);
            }

            // Aim系 → 各種
            else if (src is AimConstraint aimSrc)
            {
                if (dst is VRCAimConstraint vrcAim) CopyAimToVRCAim(aimSrc, vrcAim);
                else if (dst is LookAtConstraint lookAt) CopyAimToLookAt(aimSrc, lookAt);
                else if (dst is VRCLookAtConstraint vrcLookAt) CopyAimToVRCLookAt(aimSrc, vrcLookAt);
            }
            else if (src is VRCAimConstraint vrcAimSrc)
            {
                if (dst is AimConstraint aim) CopyVRCAimToAim(vrcAimSrc, aim);
                else if (dst is LookAtConstraint lookAt) CopyVRCAimToLookAt(vrcAimSrc, lookAt);
                else if (dst is VRCLookAtConstraint vrcLookAt) CopyVRCAimToVRCLookAt(vrcAimSrc, vrcLookAt);
            }

            // LookAt系 → 各種
            else if (src is LookAtConstraint lookAtSrc)
            {
                if (dst is VRCLookAtConstraint vrcLookAt) CopyLookAtToVRCLookAt(lookAtSrc, vrcLookAt);
                else if (dst is AimConstraint aim) CopyLookAtToAim(lookAtSrc, aim);
                else if (dst is VRCAimConstraint vrcAim) CopyLookAtToVRCAim(lookAtSrc, vrcAim);
            }
            else if (src is VRCLookAtConstraint vrcLookAtSrc)
            {
                if (dst is LookAtConstraint lookAt) CopyVRCLookAtToLookAt(vrcLookAtSrc, lookAt);
                else if (dst is AimConstraint aim) CopyVRCLookAtToAim(vrcLookAtSrc, aim);
                else if (dst is VRCAimConstraint vrcAim) CopyVRCLookAtToVRCAim(vrcLookAtSrc, vrcAim);
            }
        }

        #endregion

        #region Position Conversion Helpers

        private static void CopyPositionToVRCPosition(PositionConstraint src, VRCPositionConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCPositionConstraintWrapper;
            if (w != null) { w.PositionAtRest = src.translationAtRest; w.PositionOffset = src.translationOffset; }
        }
        private static void CopyVRCPositionToPosition(VRCPositionConstraint src, PositionConstraint dst)
        {
            dst.translationAtRest = src.PositionAtRest; dst.translationOffset = src.PositionOffset;
        }

        private static void CopyPositionToParent(PositionConstraint src, ParentConstraint dst)
        {
            dst.translationAtRest = src.translationAtRest;
            for (int i = 0; i < dst.sourceCount; i++)
                dst.SetTranslationOffset(i, src.translationOffset);
        }
        private static void CopyVRCPositionToParent(VRCPositionConstraint src, ParentConstraint dst)
        {
            dst.translationAtRest = src.PositionAtRest;
            for (int i = 0; i < dst.sourceCount; i++)
                dst.SetTranslationOffset(i, src.PositionOffset);
        }
        private static void CopyPositionToVRCParent(PositionConstraint src, VRCParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(src.sourceCount, w.SourceCount); i++)
                w.SetSourcePositionOffset(i, src.translationOffset);
        }
        private static void CopyVRCPositionToVRCParent(VRCPositionConstraint src, VRCParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(src.Sources.Count, w.SourceCount); i++)
                w.SetSourcePositionOffset(i, src.PositionOffset);
        }

        private static void CopyParentToPosition(ParentConstraint src, PositionConstraint dst)
        {
            dst.translationAtRest = src.translationAtRest;
            dst.translationOffset = src.sourceCount > 0 ? src.GetTranslationOffset(0) : Vector3.zero;
        }
        private static void CopyVRCParentToPosition(VRCParentConstraint src, PositionConstraint dst)
        {
            var w = AllConstraint.FromComponent(src) as VRCParentConstraintWrapper;
            if (w == null) return;
            dst.translationAtRest = Vector3.zero; // VRCParentConstraintにtranslationAtRest相当のプロパティがないため
            dst.translationOffset = w.SourceCount > 0 ? w.GetSourcePositionOffset(0) : Vector3.zero;
        }
        private static void CopyParentToVRCPosition(ParentConstraint src, VRCPositionConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCPositionConstraintWrapper;
            if (w == null) return;
            w.PositionAtRest = src.translationAtRest;
            w.PositionOffset = src.sourceCount > 0 ? src.GetTranslationOffset(0) : Vector3.zero;
        }
        private static void CopyVRCParentToVRCPosition(VRCParentConstraint src, VRCPositionConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCPositionConstraintWrapper;
            var srcW = AllConstraint.FromComponent(src) as VRCParentConstraintWrapper;
            if (w == null || srcW == null) return;
            w.PositionAtRest = Vector3.zero; // VRCParentConstraintにtranslationAtRest相当のプロパティがないため
            w.PositionOffset = srcW.SourceCount > 0 ? srcW.GetSourcePositionOffset(0) : Vector3.zero;
        }

        #endregion

        #region Rotation Conversion Helpers

        private static void CopyRotationToVRCRotation(RotationConstraint src, VRCRotationConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCRotationConstraintWrapper;
            if (w != null) { w.RotationAtRest = src.rotationAtRest; w.RotationOffset = src.rotationOffset; }
        }
        private static void CopyVRCRotationToRotation(VRCRotationConstraint src, RotationConstraint dst)
        {
            dst.rotationAtRest = src.RotationAtRest; dst.rotationOffset = src.RotationOffset;
        }

        private static void CopyRotationToParent(RotationConstraint src, ParentConstraint dst)
        {
            dst.rotationAtRest = src.rotationAtRest;
            for (int i = 0; i < dst.sourceCount; i++)
                dst.SetRotationOffset(i, src.rotationOffset);
        }
        private static void CopyVRCRotationToParent(VRCRotationConstraint src, ParentConstraint dst)
        {
            dst.rotationAtRest = src.RotationAtRest;
            for (int i = 0; i < dst.sourceCount; i++)
                dst.SetRotationOffset(i, src.RotationOffset);
        }
        private static void CopyRotationToVRCParent(RotationConstraint src, VRCParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(src.sourceCount, w.SourceCount); i++)
                w.SetSourceRotationOffset(i, src.rotationOffset);
        }
        private static void CopyVRCRotationToVRCParent(VRCRotationConstraint src, VRCParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(src.Sources.Count, w.SourceCount); i++)
                w.SetSourceRotationOffset(i, src.RotationOffset);
        }

        private static void CopyParentToRotation(ParentConstraint src, RotationConstraint dst)
        {
            dst.rotationAtRest = src.rotationAtRest;
            dst.rotationOffset = src.sourceCount > 0 ? src.GetRotationOffset(0) : Vector3.zero;
        }
        private static void CopyVRCParentToRotation(VRCParentConstraint src, RotationConstraint dst)
        {
            var w = AllConstraint.FromComponent(src) as VRCParentConstraintWrapper;
            if (w == null) return;
            dst.rotationAtRest = Vector3.zero; // VRCParentConstraintにrotationAtRest相当のプロパティがないため
            dst.rotationOffset = w.SourceCount > 0 ? w.GetSourceRotationOffset(0) : Vector3.zero;
        }
        private static void CopyParentToVRCRotation(ParentConstraint src, VRCRotationConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCRotationConstraintWrapper;
            if (w == null) return;
            w.RotationAtRest = src.rotationAtRest;
            w.RotationOffset = src.sourceCount > 0 ? src.GetRotationOffset(0) : Vector3.zero;
        }
        private static void CopyVRCParentToVRCRotation(VRCParentConstraint src, VRCRotationConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCRotationConstraintWrapper;
            var srcW = AllConstraint.FromComponent(src) as VRCParentConstraintWrapper;
            if (w == null || srcW == null) return;
            w.RotationAtRest = Vector3.zero; // VRCParentConstraintにrotationAtRest相当のプロパティがないため
            w.RotationOffset = srcW.SourceCount > 0 ? srcW.GetSourceRotationOffset(0) : Vector3.zero;
        }

        #endregion

        #region Parent Conversion Helpers

        private static void CopyParentToVRCParent(ParentConstraint src, VRCParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(src.sourceCount, w.SourceCount); i++)
            {
                w.SetSourcePositionOffset(i, src.GetTranslationOffset(i));
                w.SetSourceRotationOffset(i, src.GetRotationOffset(i));
            }
        }
        private static void CopyVRCParentToParent(VRCParentConstraint src, ParentConstraint dst)
        {
            var w = AllConstraint.FromComponent(src) as VRCParentConstraintWrapper;
            if (w == null) return;
            for (int i = 0; i < Mathf.Min(dst.sourceCount, w.SourceCount); i++)
            {
                dst.SetTranslationOffset(i, w.GetSourcePositionOffset(i));
                dst.SetRotationOffset(i, w.GetSourceRotationOffset(i));
            }
        }

        #endregion

        #region Scale Conversion Helpers

        private static void CopyScaleToVRCScale(ScaleConstraint src, VRCScaleConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCScaleConstraintWrapper;
            if (w != null) { w.ScaleAtRest = src.scaleAtRest; w.ScaleOffset = src.scaleOffset; }
        }
        private static void CopyVRCScaleToScale(VRCScaleConstraint src, ScaleConstraint dst)
        {
            dst.scaleAtRest = src.ScaleAtRest; dst.scaleOffset = src.ScaleOffset;
        }

        #endregion

        #region Aim / LookAt Conversion Helpers

        private static void CopyAimToVRCAim(AimConstraint src, VRCAimConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCAimConstraintWrapper;
            if (w != null)
            {
                w.AimAxis = src.aimVector; w.UpAxis = src.upVector;
                w.WorldUpVector = src.worldUpVector; w.WorldUpTransform = src.worldUpObject;
            }
        }
        private static void CopyVRCAimToAim(VRCAimConstraint src, AimConstraint dst)
        {
            dst.aimVector = src.AimAxis; dst.upVector = src.UpAxis;
            dst.worldUpVector = src.WorldUpVector; dst.worldUpObject = src.WorldUpTransform;
        }
        private static void CopyAimToLookAt(AimConstraint src, LookAtConstraint dst)
        {
            dst.worldUpObject = src.worldUpObject;
            dst.useUpObject = src.worldUpObject != null;
        }
        private static void CopyAimToVRCLookAt(AimConstraint src, VRCLookAtConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCLookAtConstraintWrapper;
            if (w != null) { w.WorldUpTransform = src.worldUpObject; w.UseUpTransform = src.worldUpObject != null; }
        }
        private static void CopyVRCAimToLookAt(VRCAimConstraint src, LookAtConstraint dst)
        {
            dst.worldUpObject = src.WorldUpTransform;
            dst.useUpObject = src.WorldUpTransform != null;
        }
        private static void CopyVRCAimToVRCLookAt(VRCAimConstraint src, VRCLookAtConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCLookAtConstraintWrapper;
            if (w != null) { w.WorldUpTransform = src.WorldUpTransform; w.UseUpTransform = src.WorldUpTransform != null; }
        }

        private static void CopyLookAtToVRCLookAt(LookAtConstraint src, VRCLookAtConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCLookAtConstraintWrapper;
            if (w != null) { w.Roll = src.roll; w.WorldUpTransform = src.worldUpObject; w.UseUpTransform = src.worldUpObject != null; }
        }
        private static void CopyVRCLookAtToLookAt(VRCLookAtConstraint src, LookAtConstraint dst)
        {
            dst.roll = src.Roll; dst.worldUpObject = src.WorldUpTransform;
        }
        private static void CopyLookAtToAim(LookAtConstraint src, AimConstraint dst)
        {
            dst.worldUpObject = src.worldUpObject;
            dst.worldUpVector = Vector3.up; // LookAtConstraintにworldUpVectorがないためデフォルト
        }
        private static void CopyLookAtToVRCAim(LookAtConstraint src, VRCAimConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCAimConstraintWrapper;
            if (w != null) { w.WorldUpTransform = src.worldUpObject; w.WorldUpVector = Vector3.up; } // LookAtConstraintにworldUpVectorがないためデフォルト
        }
        private static void CopyVRCLookAtToAim(VRCLookAtConstraint src, AimConstraint dst)
        {
            dst.worldUpObject = src.WorldUpTransform;
            dst.worldUpVector = Vector3.up; // VRCLookAtConstraintにWorldUpVectorがないためデフォルト
        }
        private static void CopyVRCLookAtToVRCAim(VRCLookAtConstraint src, VRCAimConstraint dst)
        {
            var w = AllConstraint.FromComponent(dst) as VRCAimConstraintWrapper;
            if (w != null) { w.WorldUpTransform = src.WorldUpTransform; w.WorldUpVector = Vector3.up; } // VRCLookAtConstraintにWorldUpVectorがないためデフォルト
        }

        #endregion
    }
}
