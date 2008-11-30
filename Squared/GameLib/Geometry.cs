using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.GamerServices;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Media;
using Microsoft.Xna.Framework.Net;
using Microsoft.Xna.Framework.Storage;
using Squared.Util;

namespace Squared.Game {
    public class Polygon {
        private Vector2 _Position = new Vector2(0, 0);
        protected Vector2[] _Vertices;
        protected Vector2[] _TranslatedVertices;
        protected bool _Dirty = true;
        
        public Polygon (Vector2[] vertices) {
            _Vertices = vertices;
            _TranslatedVertices = new Vector2[vertices.Length];
        }

        protected virtual void ClearDirtyFlag () {
            for (int i = 0; i < _Vertices.Length; i++) {
                _TranslatedVertices[i] = _Vertices[i] + _Position;
            }

            _Dirty = false;
        }

        public Vector2 Position {
            get {
                return _Position;
            }
            set {
                _Position = value;
                _Dirty = true;
            }
        }

        public Vector2[] GetVertices () {
            if (_Dirty)
                ClearDirtyFlag();

            return _TranslatedVertices;
        }
    }

    public delegate bool ResolveMotionPredicate (Vector2 oldVelocity, Vector2 newVelocity);

    public static class Geometry {
        public static void GetEdgeNormal (ref Vector2 first, ref Vector2 second, out Vector2 result) {
            var edgeVector = second - first;
            result = new Vector2(-edgeVector.Y, edgeVector.X);
            result.Normalize();
        }

        public static Interval<float> ProjectOntoAxis (Vector2 axis, Vector2[] vertices) {
            float d = Vector2.Dot(axis, vertices[0]);
            var result = new Interval<float>(d, d);

            for (int i = 0; i < vertices.Length; i++) {
                Vector2.Dot(ref vertices[i], ref axis, out d);

                if (d < result.Min)
                    result.Min = d;
                if (d > result.Max)
                    result.Max = d;
            }

            return result;
        }

        public static void GetPolygonAxes (Vector2[] buffer, ref int bufferCount, Vector2[] polygon) {
            if ((buffer.Length - bufferCount) < polygon.Length)
                throw new ArgumentException(
                    String.Format(
                        "Not enough remaining space in the buffer ({0}/{1}) for all the polygon's potential axes ({2}).",
                        (buffer.Length - bufferCount), buffer.Length, polygon.Length
                    ),
                    "buffer"
                );

            bool done = false;
            int i = 0;
            Vector2 firstPoint = new Vector2(), current = new Vector2();
            Vector2 previous, axis;

            while (!done) {
                previous = current;

                if (i >= polygon.Length) {
                    done = true;
                    current = firstPoint;
                } else {
                    current = polygon[i];
                }

                if (i == 0) {
                    firstPoint = current;
                    i += 1;
                    continue;
                }

                GetEdgeNormal(ref previous, ref current, out axis);

                if (Array.IndexOf(buffer, axis, 0, bufferCount) == -1) {
                    buffer[bufferCount] = axis;
                    bufferCount += 1;
                }

                i += 1;
            }
        }

        public static bool DoPolygonsIntersect (Vector2[] verticesA, Vector2[] verticesB) {
            bool result = true;

            using (var axisBuffer = BufferPool<Vector2>.Allocate(verticesA.Length + verticesB.Length)) {
                int axisCount = 0;
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    var intervalA = ProjectOntoAxis(axis, verticesA);
                    var intervalB = ProjectOntoAxis(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);

                    if (!intersects)
                        result = false;
                }
            }

            return result;
        }

        public struct ResolvedMotion {
            public bool AreIntersecting;
            public bool WouldHaveIntersected;
            public bool WillBeIntersecting;
            public Vector2 ResultVelocity;
        }

        private static void FloatSubtractor (ref float lhs, ref float rhs, out float result) {
            result = lhs - rhs;
        }

        public static bool DefaultMotionPredicate (Vector2 originalVelocity, Vector2 newVelocity) {
            return (newVelocity.Length() <= originalVelocity.Length());
        }

        public static ResolvedMotion ResolvePolygonMotion (Vector2[] verticesA, Vector2[] verticesB, Vector2 velocityA) {
            return ResolvePolygonMotion(verticesA, verticesB, velocityA, DefaultMotionPredicate);
        }

        public static Vector2 ComputePolygonCenterpoint (Vector2[] vertices) {
            Vector2 sum = new Vector2();

            for (int i = 0; i < vertices.Length; i++)
                sum += vertices[i];

            sum /= vertices.Length;
            return sum;
        }

        public static ResolvedMotion ResolvePolygonMotion (Vector2[] verticesA, Vector2[] verticesB, Vector2 velocityA, ResolveMotionPredicate predicate) {
            var result = new ResolvedMotion();
            result.AreIntersecting = true;
            result.WouldHaveIntersected = true;
            result.WillBeIntersecting = true;

            float velocityProjection;
            var velocityAxis = Vector2.Normalize(velocityA);

            Interval<float> intervalA, intervalB;
            float minDistance = float.MaxValue;

            Vector2 separationAxis = ComputePolygonCenterpoint(verticesA) - ComputePolygonCenterpoint(verticesB);

            int bufferSize = verticesA.Length + verticesB.Length + 4;
            using (var axisBuffer = BufferPool<Vector2>.Allocate(bufferSize)) {
                int axisCount = 0;

                if (velocityA.LengthSquared() > 0) {
                    axisCount += 4;
                    axisBuffer.Data[0] = Vector2.Normalize(velocityA);
                    axisBuffer.Data[1] = new Vector2(-axisBuffer.Data[0].X, axisBuffer.Data[0].Y);
                    axisBuffer.Data[2] = new Vector2(axisBuffer.Data[0].X, -axisBuffer.Data[0].Y);
                    axisBuffer.Data[3] = new Vector2(-axisBuffer.Data[0].X, -axisBuffer.Data[0].Y);
                }

                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesA);
                GetPolygonAxes(axisBuffer.Data, ref axisCount, verticesB);

                for (int i = 0; i < axisCount; i++) {
                    var axis = axisBuffer.Data[i];

                    intervalA = ProjectOntoAxis(axis, verticesA);
                    intervalB = ProjectOntoAxis(axis, verticesB);

                    bool intersects = intervalA.Intersects(intervalB);
                    if (!intersects)
                        result.AreIntersecting = false;

                    Vector2.Dot(ref axis, ref velocityA, out velocityProjection);

                    var newIntervalA = new Interval<float>(intervalA.Min + velocityProjection, intervalA.Max + velocityProjection);

                    var intersectionDistance = newIntervalA.GetDistance(intervalB, FloatSubtractor);
                    intersects = intersectionDistance < 0;
                    if (!intersects)
                        result.WouldHaveIntersected = false;

                    if ((result.WouldHaveIntersected == false) && (result.AreIntersecting == false)) {
                        result.WillBeIntersecting = false;
                        result.ResultVelocity = velocityA;
                        break;
                    }

                    if (Math.Abs(intersectionDistance) < minDistance) {
                        var minVect = axis * intersectionDistance;

                        if (Vector2.Dot(separationAxis, minVect) < 0)
                            minVect = -minVect;

                        var newVelocity = velocityA + minVect;

                        bool accept = true;

                        accept &= predicate(velocityA, newVelocity);

                        if (accept) {
                            result.ResultVelocity = newVelocity;
                            result.WillBeIntersecting = false;
                            minDistance = intersectionDistance;
                        }
                    }
                }
            }

            return result;
        }
    }
}
