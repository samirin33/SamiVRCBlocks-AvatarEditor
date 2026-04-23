using System.Collections.Generic;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class VRCFaceTrackingReferenceImageResolver
    {
        static readonly Dictionary<string, string> ShapeImageUrls = new Dictionary<string, string>
        {
            { "EyeLookOutRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookoutright-5533dc1cbbc3af08b2ed612fa07d2cf2.png" },
            { "EyeLookInRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookinright-ca641f93235eea0f2e2c82927d628199.png" },
            { "EyeLookUpRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookupright-7cb084a9c38c3bf3fe54cd0fdaadd19c.png" },
            { "EyeLookDownRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookdownright-d0b7955ac9b1345f56744eaaa36eb39d.png" },
            { "EyeLookOutLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookoutleft-d593332caf045ea90af217cd0cc12c4d.png" },
            { "EyeLookInLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookinleft-851bf07de3bdaa09ec42df10027f7aa6.png" },
            { "EyeLookUpLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookupleft-111e400988700ae3b22f453f8dddaea4.png" },
            { "EyeLookDownLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyelookdownleft-a3c4629e1ae4986833c5abb0c557d62d.png" },
            { "EyeClosedRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyeclosedright-365e7da6a90a07a8a009c871cf0ec199.png" },
            { "EyeClosedLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyeclosedleft-c5482a1bdb0c60057d87a625d2b855c5.png" },
            { "EyeSquintRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyesquintright-7bd568159c976d60540e65c8f4d20cee.png" },
            { "EyeSquintLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyesquintleft-843bff573d4df5365b07bfc2e54a2225.png" },
            { "EyeWideRight", "https://docs.vrcft.io/assets/images/avatar_ref_eyewideright-cacab15ddac75cdf665882cfc996c0ba.png" },
            { "EyeWideLeft", "https://docs.vrcft.io/assets/images/avatar_ref_eyewideleft-f37aea43f00d998be5f70334ac1978ba.png" },
            { "EyeDilation", "https://docs.vrcft.io/assets/images/avatar_ref_eyedilation-a8b6b06335f3ac37fd6aaf2b09fe76fb.png" },
            { "BrowPinchRight", "https://docs.vrcft.io/assets/images/avatar_ref_browpinchright-7aace5292266cd868164e9ef794714e5.png" },
            { "BrowPinchLeft", "https://docs.vrcft.io/assets/images/avatar_ref_browpinchleft-3a67228b250a252d7dbba8417a8c13b2.png" },
            { "BrowLowererRight", "https://docs.vrcft.io/assets/images/avatar_ref_browlowererright-c89ae3d9198ffdffb2c977e47f1502f1.png" },
            { "BrowLowererLeft", "https://docs.vrcft.io/assets/images/avatar_ref_browlowererleft-c121b2dfb8c55d8a37c282fdacbffa52.png" },
            { "BrowInnerUpRight", "https://docs.vrcft.io/assets/images/avatar_ref_browinnerupright-2767107458f8e162fdf48c0b1386b378.png" },
            { "BrowInnerUpLeft", "https://docs.vrcft.io/assets/images/avatar_ref_browinnerupleft-44b343a0ae203bf75fb58b1db77356f8.png" },
            { "BrowOuterUpRight", "https://docs.vrcft.io/assets/images/avatar_ref_browouterupright-0cb21a271859051ce888c96780cdae5d.png" },
            { "BrowOuterUpLeft", "https://docs.vrcft.io/assets/images/avatar_ref_browouterupleft-89e484fe933226a8bd2588855c2e6cdb.png" },
            { "NoseSneerRight", "https://docs.vrcft.io/assets/images/avatar_ref_nosesneerright-52f2712456ffaaf3214f37d95b129c7f.png" },
            { "NoseSneerLeft", "https://docs.vrcft.io/assets/images/avatar_ref_nosesneerleft-e8acff077960fc24e31590a19b5f3e6c.png" },
            { "CheekSquintRight", "https://docs.vrcft.io/assets/images/avatar_ref_cheeksquintright-5936a60f72e6425f4bfd9d3d14a71204.png" },
            { "CheekSquintLeft", "https://docs.vrcft.io/assets/images/avatar_ref_cheeksquintleft-1e640b773d342363ff81ede8b1141c7c.png" },
            { "JawOpen", "https://docs.vrcft.io/assets/images/avatar_ref_jawopen-2b85979b033fe1c62604f8b324bdcb8b.png" },
            { "MouthClosed", "https://docs.vrcft.io/assets/images/avatar_ref_mouth_closed_explain-29a9addb2e5968a28715b84408a9959d.png" },
            { "JawClench", "https://docs.vrcft.io/assets/images/avatar_ref_jawclench-747137b44acd0287e12b497517720aea.png" },
            { "JawMandibleRaise", "https://docs.vrcft.io/assets/images/avatar_ref_jawmandibleraise-d6465af008b9ac40b61af452030f0e52.png" },
            { "LipSuck", "https://docs.vrcft.io/assets/images/avatar_ref_lipsuck-3de611cb3f808d3101728063632ba24d.png" },
            { "LipFunnel", "https://docs.vrcft.io/assets/images/avatar_ref_lipfunnel-6b0c6d3d2035b43951d8a5ad0274af39.png" },
            { "LipPucker", "https://docs.vrcft.io/assets/images/avatar_ref_lippucker-f3cf86fbb07495675bd157174833f6d3.png" },
            { "MouthUpperUp", "https://docs.vrcft.io/assets/images/avatar_ref_mouthupperup-f0e64b59bb64ad041b51ce5bf2ddc070.png" },
            { "MouthLowerDown", "https://docs.vrcft.io/assets/images/avatar_ref_mouthlowerdown-cfcbf97a7db9d5ee4852eae97ff78f79.png" },
            { "MouthOpen", "https://docs.vrcft.io/assets/images/avatar_ref_mouthopen-c63b7541968f553f864074bc8f7bb535.png" },
            { "MouthSmileRight", "https://docs.vrcft.io/assets/images/avatar_ref_mouthsmileright-fac040e4efaa150d029b1bba25ff3a77.png" },
            { "MouthSmileLeft", "https://docs.vrcft.io/assets/images/avatar_ref_mouthsmileleft-2870a19ffd8132ca302b9c212ce2914f.png" },
            { "MouthSadRight", "https://docs.vrcft.io/assets/images/avatar_ref_mouthsadright-bede689f003cec73f554204deaa94826.png" },
            { "MouthSadLeft", "https://docs.vrcft.io/assets/images/avatar_ref_mouthsadleft-27c3bc140d0dfaf01bf8f4a4c3195fb1.png" },
            { "MouthFrownRight", "https://docs.vrcft.io/assets/images/avatar_ref_mouthfrownright-de0f679ee1328db82fa9707a10b202d3.png" },
            { "MouthFrownLeft", "https://docs.vrcft.io/assets/images/avatar_ref_mouthfrownleft-f3912457bbca67b481eabc94f46320be.png" },
            { "MouthStretchRight", "https://docs.vrcft.io/assets/images/avatar_ref_mouthstretchright-a5d0ae28e7f2e35827df14f6adfe240f.png" },
            { "MouthStretchLeft", "https://docs.vrcft.io/assets/images/avatar_ref_mouthstretchleft-8fc5d53d1bc6e91e301f4c78160fdaf4.png" },
            { "TongueOut", "https://docs.vrcft.io/assets/images/avatar_ref_tongueout-5f3e455ea1f1737b102e5a5871944bbe.png" },
            { "TongueRoll", "https://docs.vrcft.io/assets/images/avatar_ref_tongueroll-985299c17c57ed6f7391ccd40befb954.png" },
            { "TongueTwistRight", "https://docs.vrcft.io/assets/images/avatar_ref_tonguetwistright-f9521f1f67131bacc7299180e0bb8520.png" },
            { "TongueTwistLeft", "https://docs.vrcft.io/assets/images/avatar_ref_tonguetwistleft-512cc07ed2625fa798a2320cc79277c5.png" },
            { "SoftPalateClose", "https://docs.vrcft.io/assets/images/avatar_ref_softpalateclose-fe30c16e27013225c42225e289db6687.png" },
            { "ThroatSwallow", "https://docs.vrcft.io/assets/images/avatar_ref_throatswallow-ada05850509c090ebbb37173b270f524.png" },
            { "NeckFlexRight", "https://docs.vrcft.io/assets/images/avatar_ref_neckflexright-765c80e81c033341d78f0b036976e52c.png" },
            { "NeckFlexLeft", "https://docs.vrcft.io/assets/images/avatar_ref_neckflexleft-f9801dccd0b955ce9ec4430f225cdcbc.png" },
            { "BrowDownRight", "https://docs.vrcft.io/assets/images/avatar_ref_browdownright-89547c68bfdba9a83726adc493786cd2.png" },
            { "BrowDownLeft", "https://docs.vrcft.io/assets/images/avatar_ref_browdownleft-39b4ac170e916502b10ac8e4287127e8.png" },
            { "BrowInnerUp", "https://docs.vrcft.io/assets/images/avatar_ref_browinnerup-fc486d25e2821ca736b55e0aa14fa7e6.png" },
            { "BrowUp", "https://docs.vrcft.io/assets/images/avatar_ref_browup-6d629a65997762f2ffa0f016131c66cd.png" },
            { "NoseSneer", "https://docs.vrcft.io/assets/images/avatar_ref_nosesneer-5247651e21e4e67dfd6e51bb5f6bcc61.png" },
            { "CheekSquint", "https://docs.vrcft.io/assets/images/avatar_ref_cheeksquint-1a9ee5df24ab7928ff390ae8473dc965.png" },
        };

        public static bool TryGetImageUrls(string parameterName, out List<string> urls)
        {
            urls = new List<string>();

            switch (parameterName)
            {
                case "v2/EyeLeftX": Add(urls, "EyeLookOutLeft", "EyeLookInLeft"); break;
                case "v2/EyeLeftY": Add(urls, "EyeLookUpLeft", "EyeLookDownLeft"); break;
                case "v2/EyeRightX": Add(urls, "EyeLookOutRight", "EyeLookInRight"); break;
                case "v2/EyeRightY": Add(urls, "EyeLookUpRight", "EyeLookDownRight"); break;
                case "v2/EyeLidLeft": Add(urls, "EyeClosedLeft", "EyeWideLeft"); break;
                case "v2/EyeLidRight": Add(urls, "EyeClosedRight", "EyeWideRight"); break;
                case "v2/EyeSquintLeft": Add(urls, "EyeSquintLeft"); break;
                case "v2/EyeSquintRight": Add(urls, "EyeSquintRight"); break;
                case "v2/PupilDilation": Add(urls, "EyeDilation"); break;
                case "v2/BrowPinchLeft": Add(urls, "BrowPinchLeft"); break;
                case "v2/BrowPinchRight": Add(urls, "BrowPinchRight"); break;
                case "v2/BrowLowererLeft": Add(urls, "BrowLowererLeft"); break;
                case "v2/BrowLowererRight": Add(urls, "BrowLowererRight"); break;
                case "v2/BrowInnerUpLeft": Add(urls, "BrowInnerUpLeft"); break;
                case "v2/BrowInnerUpRight": Add(urls, "BrowInnerUpRight"); break;
                case "v2/BrowOuterUpLeft": Add(urls, "BrowOuterUpLeft"); break;
                case "v2/BrowOuterUpRight": Add(urls, "BrowOuterUpRight"); break;
                case "v2/NoseSneerLeft": Add(urls, "NoseSneerLeft"); break;
                case "v2/NoseSneerRight": Add(urls, "NoseSneerRight"); break;
                case "v2/CheekSquintLeft": Add(urls, "CheekSquintLeft"); break;
                case "v2/CheekSquintRight": Add(urls, "CheekSquintRight"); break;
                case "v2/JawOpen": Add(urls, "JawOpen"); break;
                case "v2/MouthClosed": Add(urls, "MouthClosed"); break;
                case "v2/JawClench1": Add(urls, "JawClench"); break;
                case "v2/JawMandibleRaise1": Add(urls, "JawMandibleRaise"); break;
                case "v2/LipSuck": Add(urls, "LipSuck"); break;
                case "v2/LipFunnel": Add(urls, "LipFunnel"); break;
                case "v2/LipPucker": Add(urls, "LipPucker"); break;
                case "v2/MouthUpperUp": Add(urls, "MouthUpperUp"); break;
                case "v2/MouthLowerDown": Add(urls, "MouthLowerDown"); break;
                case "v2/MouthOpen": Add(urls, "MouthOpen"); break;
                case "v2/MouthSmileRight": Add(urls, "MouthSmileRight"); break;
                case "v2/MouthSmileLeft": Add(urls, "MouthSmileLeft"); break;
                case "v2/MouthSadRight": Add(urls, "MouthSadRight"); break;
                case "v2/MouthSadLeft": Add(urls, "MouthSadLeft"); break;
                case "v2/MouthFrownRight": Add(urls, "MouthFrownRight"); break;
                case "v2/MouthFrownLeft": Add(urls, "MouthFrownLeft"); break;
                case "v2/MouthStretchRight": Add(urls, "MouthStretchRight"); break;
                case "v2/MouthStretchLeft": Add(urls, "MouthStretchLeft"); break;
                case "v2/TongueOut": Add(urls, "TongueOut"); break;
                case "v2/TongueRoll": Add(urls, "TongueRoll"); break;
                case "v2/TongueTwistRight": Add(urls, "TongueTwistRight"); break;
                case "v2/TongueTwistLeft": Add(urls, "TongueTwistLeft"); break;
                case "v2/SoftPalateClose": Add(urls, "SoftPalateClose"); break;
                case "v2/ThroatSwallow": Add(urls, "ThroatSwallow"); break;
                case "v2/NeckFlexRight": Add(urls, "NeckFlexRight"); break;
                case "v2/NeckFlexLeft": Add(urls, "NeckFlexLeft"); break;
                case "v2/BrowDownRight": Add(urls, "BrowDownRight"); break;
                case "v2/BrowDownLeft": Add(urls, "BrowDownLeft"); break;
                case "v2/BrowInnerUp": Add(urls, "BrowInnerUp"); break;
                case "v2/BrowUp": Add(urls, "BrowUp"); break;
                case "v2/NoseSneer": Add(urls, "NoseSneer"); break;
                case "v2/CheekSquint": Add(urls, "CheekSquint"); break;
                default: return false;
            }

            return urls.Count > 0;
        }

        static void Add(List<string> target, params string[] shapeNames)
        {
            for (int i = 0; i < shapeNames.Length; i++)
            {
                if (ShapeImageUrls.TryGetValue(shapeNames[i], out var url))
                    target.Add(url);
            }
        }
    }
}
