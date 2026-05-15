using UnityEngine;

namespace DungKeeper
{
    [ExecuteAlways]
    public class RoomGenerator : MonoBehaviour
    {
        public float width = 14f;
        public float depth = 14f;
        public float height = 2.5f;

        private void Awake() => BuildRoom();

        public void BuildRoom()
        {
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            var stone = MakeMat(new Color(0.22f, 0.19f, 0.17f));
            var floor  = MakeMat(new Color(0.28f, 0.24f, 0.20f));

            Slab("Floor",     new Vector3(0,          0.1f,       0),          new Vector3(width, 0.2f,  depth),  floor);
            Slab("WallNorth", new Vector3(0,          height/2f,  depth/2f),   new Vector3(width, height, 0.3f),  stone);
            Slab("WallSouth", new Vector3(0,          height/2f, -depth/2f),   new Vector3(width, height, 0.3f),  stone);
            Slab("WallEast",  new Vector3( width/2f,  height/2f,  0),          new Vector3(0.3f,  height, depth), stone);
            Slab("WallWest",  new Vector3(-width/2f,  height/2f,  0),          new Vector3(0.3f,  height, depth), stone);
        }

        private void Slab(string id, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = id;
            go.transform.SetParent(transform);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.isStatic = true;
        }

        private static Material MakeMat(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
