using System.Numerics;

namespace BrickEngine.Example
{
    sealed class Transform
    {
        #region Fields

        #region Local
        private Vector3 localPosition = new Vector3();
        private Quaternion localRotation = Quaternion.Identity;
        private Vector3 localScale = new Vector3(1, 1, 1);
        private Transform? parent;
        //private readonly List<Transform> children = new List<Transform>();

        #endregion

        #endregion

        #region Properties

        public Transform? Parent
        {
            get => parent;
            set
            {
                parent = value;
            }
        }

        //public static bool IsChildOf(Transform scr, Transform potentialChild)
        //{
        //    bool isChild = false;
        //    for (int i = 0; i < scr.children.Count; i++)
        //    {
        //        isChild = scr.children[i] == potentialChild;

        //        if (isChild)
        //        {
        //            break;
        //        }
        //    }
        //    return isChild;
        //}

        //public static bool IsChildOfRecursive(Transform scr, Transform potentialChild)
        //{
        //    bool isChild = false;
        //    for (int i = 0; i < scr.children.Count; i++)
        //    {
        //        isChild = scr.children[i] == potentialChild;
        //        //Depth first search
        //        if (!isChild)
        //        {
        //            isChild = IsChildOfRecursive(scr.children[i], potentialChild);
        //        }
        //        if (isChild)
        //        {
        //            break;
        //        }
        //    }
        //    return isChild;
        //}

        //public IReadOnlyList<Transform> Children
        //{
        //    get { return children; }
        //}

        #region Local
        public Vector3 LocalPosition
        {
            get => localPosition; set
            {
                localPosition = value;
            }
        }
        public Quaternion LocalRotation
        {
            get => localRotation; set
            {
                localRotation = value;
            }
        }
        public Vector3 LocalScale
        {
            get => localScale; set
            {
                localScale = value;
            }
        }
        #endregion

        #region World

        public Vector3 Position
        {
            get
            {
                if (parent != null)
                {
                    return Vector3.Transform(LocalPosition, parent.WorldMatrix);
                }

                return LocalPosition;
            }

            set
            {
                if (parent != null)
                {
                    Matrix4x4.Invert(parent.WorldMatrix, out var inverse);
                    LocalPosition = Vector3.Transform(value - parent.Position, inverse);
                }
                else
                {
                    LocalPosition = value;
                }
            }
        }

        public Quaternion Rotation
        {
            get
            {
                if (parent != null)
                {
                    return parent.Rotation * LocalRotation;
                }
                else
                {
                    return LocalRotation;
                }
            }
            set
            {
                if (parent != null)
                {
                    LocalRotation = Quaternion.Inverse(parent.Rotation) * value;
                }
                else
                {
                    LocalRotation = value;
                }
            }
        }

        public Vector3 Scale
        {
            get
            {
                if (parent != null)
                {
                    return localScale * parent.Scale;
                }
                return localScale;
            }

            set
            {
                if (parent != null)
                {
                    LocalScale = new Vector3(
                    value.X / parent.Scale.X,
                    value.Y / parent.Scale.Y,
                    value.Z / parent.Scale.Z);
                }
                else
                {
                    LocalScale = value;
                }
            }
        }
        #endregion

        #endregion


        #region Matrix
        public Matrix4x4 LocalWorldMatrix
        {
            get
            {
                return Matrix4x4.CreateScale(LocalScale)
                        * Matrix4x4.CreateFromQuaternion(LocalRotation)
                        * Matrix4x4.CreateTranslation(LocalPosition);
            }
            set
            {
                Matrix4x4.Decompose(value, out localScale, out localRotation, out localPosition);
            }
        }

        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (parent != null)
                {
                    return LocalWorldMatrix * parent.WorldMatrix;
                }
                return LocalWorldMatrix;
            }
            set
            {
                if (Matrix4x4.Decompose(value, out var scale, out var rotation, out var position))
                {
                    Scale = scale;
                    Rotation = rotation;
                    Position = position;
                }
            }
        }

        #endregion


        #region Unit Axes

        #region Local
        public Vector3 LocalForward
        {
            get
            {
                return Vector3.Transform(Vector3.UnitZ, LocalRotation);
            }
        }
        public Vector3 LocalUp
        {
            get
            {
                return Vector3.Transform(Vector3.UnitY, LocalRotation);
            }
        }
        public Vector3 LocalRight
        {
            get
            {
                return Vector3.Transform(Vector3.UnitX, LocalRotation);
            }
        }


        #endregion

        #region World
        public Vector3 Forward
        {
            get
            {
                return Vector3.Transform(Vector3.UnitZ, Rotation);
            }
        }
        public Vector3 Up
        {
            get
            {
                return Vector3.Transform(Vector3.UnitY, Rotation);
            }
        }

        public Vector3 Right
        {
            get
            {
                return Vector3.Transform(Vector3.UnitX, Rotation);
            }
        }

        #endregion

        #endregion

        public static Transform Create()
        {
            return new Transform();
        }

        public static Transform Create(System.Numerics.Matrix4x4 matrix)
        {
            var t = new Transform();
            t.WorldMatrix = matrix;
            return t;
        }

        internal Transform Copy()
        {
            var t = new Transform();
            t.WorldMatrix = WorldMatrix;
            t.parent = parent;
            return t;
        }

        internal Transform CopyLocal()
        {
            var t = new Transform();
            t.LocalWorldMatrix = LocalWorldMatrix;
            return t;
        }
    }
}
