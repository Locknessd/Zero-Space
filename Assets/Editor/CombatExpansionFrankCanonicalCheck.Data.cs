namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionFrankCanonicalCheck
    {
        // Explicit identities captured from source metadata and controller motion references.
        // Frank row: label, rig, original clip, Mankey clip, Pepe clip, original controller, derived controllers.
        static readonly string[][] FrankLinks =
        {
            new[]
            {
                "2Handed",
                "0ecbaf72cc6563b3788f1d0796e10828",
                "23f690a146a2b8e418e1ab3e0aea5064",
                "0bdec02c684bfaf45a09de44c51a1302",
                "9c35a5274953fd6efbf06df0defbdf9c",
                "ff1742dd3a3feaf45aa362e422f0f91e",
                "f8c751c5a7d4ffbe794e38ae2a6b2e9f",
                "5ecefe89899b139a1a5f06855092283e",
            },
            new[]
            {
                "Assassin",
                "887c136fb6189f3bf8919955b7a6ead6",
                "2c73e3dd92553e942b71dcb39237c52c",
                "010f31f1d8757e445be14398d53c516f",
                "db89ee44446186752998141b56ca836a",
                "7d65a60165d78d945bcb292f076854d8",
                "e4db2a0a5a351227b8469520ce256b10",
                "2b3e39682c3eb8374b92e36798d6494c",
            },
            new[]
            {
                "Dual",
                "1d628956d53416971b3e2753725ecfb4",
                "3f4878697c191ea46886ad1a0c45435e",
                "e762c1dee3c6fd4158d7c574da543530",
                "1ad3d996a0d19d8e6a467d4df0bbfb72",
                "c72d3018a04a9014f907ce83dfd7ceee",
                "0fd811574bfe4b8af93269621a1d43fe",
                "288ea083cd26a743090024bb6efecb6d",
            },
            new[]
            {
                "GreatSword",
                "f9d4896795dbdf2eaa74e2b782a33753",
                "60816012dd2c96449be704f9ba3e7bdc",
                "d7f6ec4fe18d56ed4b64b0a65410cdaa",
                "32a144720df8bb1cfb3dc58b6eb1b05b",
                "fc05081fee46d6f4ea1d9243f6b9f53d",
                "4754b4cb68004534696332aad9929ad8",
                "5bbc2a3bfd0941b7fad02ab33d3dfdfe",
            },
            new[]
            {
                "Katana",
                "a3b4748d4a8e7dfd4b510849d3ea6340",
                "7e2666062ee2d55429beea6899bf89dc",
                "6b18ba42872719f6c9bc12b0c3f51ade",
                "9361f4e2bb5ead06aa81604f8381a8b9",
                "f3d1f66ddee40b940ac27718333fb148",
                "a627704c29545516e98461690a27c161",
                "0bdc17397cf180cbdae209b72d219092",
            },
            new[]
            {
                "Spear",
                "a74c88880dbb6d6a5b23708a0ca7d294",
                "378caf935aaa27a48a2ce063a9b9f60c",
                "c3bf65b3b61bc4d9c853c894a6b5c888",
                "e3d0e60f2b1771925ac4af64e74863a1",
                "5d9c18aa8a1633344b32fc8aba5eb843",
                "9f074467d3fa077cfaddd1802c01cb06",
                "ede44fd0f1a130a2c95e9986ad600b8d",
            },
            new[]
            {
                "Warrior",
                "2cc53f050cdcab3c99304b15928061e4",
                "d8dd30cce5c56764b847fc7ea75a2ff3",
                "704a3636bd202b4a8a22c98bc3ff67e3",
                "55df8da958feec5379886b57b8fdaa22",
                "4a52a7322548bbb4ca99e5746988985c",
                "5c3a7e3a7a3f32a7c8ba0b061932bb06",
                "63286d66af30973d2a32f29d497bc5b8",
            },
        };
        static readonly string[][] WarriorLinks =
        {
            new[]
            {
                "Warrior/Mankey",
                "815993e08bc1e7bc6ad4c04db31399c9",
                "8f109034448249a52a6584997acc18ff",
                "c2a099669f30a27f69537b24724579ed",
            },
            new[]
            {
                "Warrior/Pepe",
                "51ecae1bd49859bfc96cfd34496401c5",
                "0bf36a47ad0088c1a9ce5d12100dcdb1",
                "02bdc51876988d20487a09d2adf45d30",
            },
        };
        sealed class SourceId
        {
            public readonly int group;
            public readonly int step;
            public readonly string guid;
            public readonly long id;
            public SourceId(int group, int step, string guid, long id)
            {
                this.group = group;
                this.step = step;
                this.guid = guid;
                this.id = id;
            }
        }
        static readonly SourceId[] InsaneSources =
        {
            new SourceId(1, 0, "c80ead7ac0634a539b64f1ce6f3a0efc", -3015926243572891356L),
            new SourceId(1, 1, "bd2695d2d15746df93b4a756dab34b6e", -7168219488691840090L),
            new SourceId(1, 2, "c96c37c00670422e8a1cc8fe7e152199", 621988743821468482L),
            new SourceId(1, 3, "d891711e0b634c249be2ba9b228db3d0", 8054657191847103693L),
            new SourceId(1, -1, "b91dee60a2284a49ba9c6064f802ff4a", 1785527927380778294L),
            new SourceId(2, 0, "eebf2ce8ce134259b8f0ccc7a13f9a3f", -4983947132921216529L),
            new SourceId(2, 1, "63baa1474944405b830f557996c07cc9", -5561146804881996817L),
            new SourceId(2, 2, "d71ef335ea554b6998e0f66ae7d77e0f", -1114461008604559621L),
            new SourceId(2, 3, "5b9dc0b9832a41d1a8f27698062ee0ff", 7634506225472288704L),
            new SourceId(2, 4, "dbb43e4f833d4d2c819d7f63502c311c", 5915604085484973052L),
            new SourceId(2, 5, "dfd10bdc4b98437a80edd5c1b41fc7fb", -4079185345734941493L),
            new SourceId(2, 6, "9017be394ac64594b78642a34ace49d3", -1874523524414390373L),
            new SourceId(2, -1, "1c2e7d083dc54958be3c56ecb04e4851", 5031028613507991798L),
            new SourceId(3, 0, "d2c6002ace4e476eae9c5c856108bc68", 2547221489797493871L),
            new SourceId(3, 1, "84afb935b35545d2bc5f652cb4ed7144", 5265963377054049447L),
            new SourceId(3, 2, "bdeb758fb4aa4dabb1e5b961269d4b16", -3829734982352317823L),
            new SourceId(3, 3, "be7be65d29434baeabc77b90c664eedd", -3190073506354467867L),
            new SourceId(3, 4, "92663198d7144b8cbb800e9ccba938d8", -3703437179968889211L),
            new SourceId(3, 5, "c79bc1ee3fe3402693f24b64ba6f10e8", 3675911952298850722L),
            new SourceId(3, -1, "18352db5f451430eace21d47fa873349", -7810264698804697806L),
        };
    }
}
