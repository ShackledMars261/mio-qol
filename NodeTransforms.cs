using MioGame;

namespace QoL.Util
{
    /// <summary>World-space helpers for scene nodes (<see cref="Node2"/>).</summary>
    internal static unsafe class NodeTransforms
    {
        /// <summary>
        /// The node's world position. Uses <see cref="Node2.world_transform"/> when the engine
        /// has computed it (<see cref="Node2.Flags.Has_first_world_transform"/>). Otherwise it
        /// builds the position from local transforms up the parent chain. Hidden or disabled
        /// nodes, such as a gate that hasn't been unlocked yet, can have an all-zero world matrix.
        /// </summary>
        internal static Vec_float_3 WorldPosition(Node2* node)
        {
            if ((node->flags & Node2.Flags.Has_first_world_transform) != 0)
            {
                var m = node->world_transform.col(3);
                if (!IsNearOrigin(m)) return m;
            }

            Vec_float_3 p = node->_transform.translation;
            if ((node->flags & Node2.Flags.World_space) != 0) return p;

            for (Node2* a = node->parent; a != null; a = a->parent)
            {
                ref Aff t = ref a->_transform;
                p = Add(t.translation, Rotate(t.rotation, Scale(t.scale, p)));
                if ((a->flags & Node2.Flags.World_space) != 0) break;
            }
            return p;
        }

        internal static bool IsNearOrigin(Vec_float_3 v) => v.x * v.x + v.y * v.y + v.z * v.z < 1f;

        internal static string Format(Vec_float_3 v) => $"({v.x:0.0}, {v.y:0.0}, {v.z:0.0})";

        private static Vec_float_3 Rotate(Quat q, Vec_float_3 v)
        {
            // v' = v + 2w(p x v) + 2 p x (p x v)
            var c1 = Cross(q.p, v);
            var c2 = Cross(q.p, c1);
            return Make(v.x + 2 * (q.w * c1.x + c2.x),
                        v.y + 2 * (q.w * c1.y + c2.y),
                        v.z + 2 * (q.w * c1.z + c2.z));
        }

        private static Vec_float_3 Cross(Vec_float_3 a, Vec_float_3 b) =>
            Make(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        private static Vec_float_3 Scale(Vec_float_3 s, Vec_float_3 v) => Make(s.x * v.x, s.y * v.y, s.z * v.z);

        private static Vec_float_3 Add(Vec_float_3 a, Vec_float_3 b) => Make(a.x + b.x, a.y + b.y, a.z + b.z);

        private static Vec_float_3 Make(float x, float y, float z)
        {
            Vec_float_3 v = default;
            v.x = x; v.y = y; v.z = z;
            return v;
        }
    }
}