using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Squared.Render;
using Squared.Game;

namespace Pong {
    public class PongExample : MultithreadedGame {
        GraphicsDeviceManager Graphics;
        PongMaterials Materials;
        Playfield Playfield;
        Paddle[] Paddles;
        int[] Scores = new int[2];
        Ball Ball;
        SpriteFont Font;
        SpriteBatch SpriteBatch;
        RenderTarget2D TrailBuffer;
        DepthStencilBuffer DefaultDepthStencilBuffer;
        bool FirstFrame = true;
        const int TrailScale = 2;

        public PongExample() {
            Graphics = new GraphicsDeviceManager(this);
            Content.RootDirectory = "Content";

            Graphics.SynchronizeWithVerticalRetrace = true;
            Graphics.PreferMultiSampling = true;
            IsFixedTimeStep = false;
        }

        protected override void Initialize() {
            base.Initialize();

            // Remember the depth/stencil buffer created with the graphics device, so we can restore it later
            DefaultDepthStencilBuffer = GraphicsDevice.DepthStencilBuffer;

            // Create a render target to use for rendering trails for the ball and paddles
            TrailBuffer = new RenderTarget2D(
                Graphics.GraphicsDevice,
                Graphics.GraphicsDevice.Viewport.Width / TrailScale,
                Graphics.GraphicsDevice.Viewport.Height / TrailScale,
                1, SurfaceFormat.Color, RenderTargetUsage.PreserveContents
            );

            Playfield = new Playfield {
                Bounds = new Bounds(new Vector2(-32, 24), new Vector2(Graphics.GraphicsDevice.Viewport.Width + 32, Graphics.GraphicsDevice.Viewport.Height - 24))
            };

            ResetPlayfield(0);
        }

        protected override void LoadContent() {
            Materials = new PongMaterials(Content) {
                ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                    0, GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, 0,
                    0, 1
                )
            };

            Font = Content.Load<SpriteFont>("Tahoma");

            SpriteBatch = new SpriteBatch(GraphicsDevice);
        }

        public void ResetPlayfield (int loserIndex) {
            Paddles = new Paddle[] { 
                new Paddle {
                    Bounds = new Bounds(
                        new Vector2(24, Playfield.Bounds.Center.Y - 48),
                        new Vector2(24 + 16, Playfield.Bounds.Center.Y + 48)
                    ),
                    Playfield = this.Playfield
                },
                new Paddle {
                    Bounds = new Bounds(
                        new Vector2(GraphicsDevice.Viewport.Width - 24 - 16, Playfield.Bounds.Center.Y - 48), 
                        new Vector2(GraphicsDevice.Viewport.Width - 24, Playfield.Bounds.Center.Y + 48)
                    ),
                    Playfield = this.Playfield
                }
            };

            var random = new Random();
            var velocity = new Vector2(loserIndex == 0 ? 1 : -1, (float)random.NextDouble(-1, 1));
            velocity.Normalize();
            if (Math.Abs(velocity.Y) < 0.15f)
                velocity.Y += Math.Sign(velocity.Y) * 0.15f;
            velocity *= 4;

            Ball = new Ball {
                Position = Playfield.Bounds.Center,
                Velocity = velocity,
                Playfield = this.Playfield,
                Radius = 8.0f
            };
        }

        protected override void UnloadContent() {
        }

        protected override void Update(GameTime gameTime) {
            var padState1 = GamePad.GetState(PlayerIndex.One);
            var padState2 = GamePad.GetState(PlayerIndex.Two);
            var keyboardState = Keyboard.GetState();

            if ((padState1.DPad.Down == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.S)))
                Paddles[0].Velocity = new Vector2(0, 4);
            else if ((padState1.DPad.Up == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.W)))
                Paddles[0].Velocity = new Vector2(0, -4);
            else
                Paddles[0].Velocity = Vector2.Zero;

            if ((padState2.DPad.Down == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Down)))
                Paddles[1].Velocity = new Vector2(0, 4);
            else if ((padState2.DPad.Up == ButtonState.Pressed) ||
                (keyboardState.IsKeyDown(Keys.Up)))
                Paddles[1].Velocity = new Vector2(0, -4);
            else
                Paddles[1].Velocity = Vector2.Zero;

            foreach (var paddle in Paddles)
                paddle.Update();

            Ball.Update(Paddles);

            if (Ball.Position.X < Paddles[0].Bounds.TopLeft.X - 8) {
                Scores[1] += 1;
                ResetPlayfield(0);
            } else if (Ball.Position.X > Paddles[1].Bounds.BottomRight.X + 8) {
                Scores[0] += 1;
                ResetPlayfield(1);
            }

            base.Update(gameTime);
        }

        public override void Draw(GameTime gameTime, Frame frame) {
            ClearBatch.AddNew(frame, 4, Color.Black, Materials.Clear);

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 5, Materials.ScreenSpaceGeometry)) {
                Primitives.GradientFilledQuad(
                    gb, Vector2.Zero, new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height),
                    Color.DarkSlateGray, Color.DarkSlateGray,
                    Color.SlateBlue, Color.SlateBlue
                );
            }

            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 6, Materials.ScreenSpaceGeometry)) {
                var alphaBlack = new Color(0, 0, 0, 192);
                var alphaBlack2 = new Color(0, 0, 0, 64);

                Primitives.BorderedBox(
                    gb, 
                    Playfield.Bounds.TopLeft + new Vector2(32 + 24, 0), 
                    Playfield.Bounds.BottomRight + new Vector2(-32 - 24, 0), 
                    alphaBlack2, alphaBlack, 24
                );
            }

            // Render the contents of the trail buffer to the screen using additive blending
            using (var bb = BitmapBatch.New(frame, 7, Materials.Trail)) {
                bb.Add(new BitmapDrawCall(
                    TrailBuffer, Vector2.Zero, (float)TrailScale
                ));
            }

            // Render the paddles and ball to both the framebuffer and the trail buffer (note the different layer values)
            using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 8, Materials.ScreenSpaceGeometry))
            using (var trailBatch = PrimitiveBatch<VertexPositionColor>.New(frame, 2, Materials.ScreenSpaceGeometry)) {
                foreach (var paddle in Paddles) {
                    Primitives.FilledQuad(
                        gb, paddle.Bounds.TopLeft, paddle.Bounds.BottomRight, Color.White
                    );
                    Primitives.BorderedBox(
                        gb, paddle.Bounds.TopLeft, paddle.Bounds.BottomRight, Color.Black, Color.Black, 2.25f
                    );

                    Primitives.FilledQuad(
                        trailBatch, paddle.Bounds.TopLeft, paddle.Bounds.BottomRight, Color.White
                    );
                }

                Primitives.FilledRing(gb, Ball.Position, 0.0f, Ball.Radius, Color.White, Color.White);
                Primitives.FilledRing(gb, Ball.Position, Ball.Radius - 0.5f, Ball.Radius + 1.5f, Color.Black, Color.Black);

                Primitives.FilledRing(trailBatch, Ball.Position, 0.0f, Ball.Radius, Color.White, Color.White);
            }

            // Render the score values using a stringbatch (unfortunately this uses spritebatch to render spritefonts :( )
            using (var sb = StringBatch.New(frame, 9, Materials.ScreenSpaceBitmap, SpriteBatch, Font)) {
                var drawCall = new StringDrawCall(
                    String.Format("Player 1: {0:00}", Scores[0]),
                    new Vector2(16, 16),
                    Color.White
                );

                sb.Add(drawCall.Shadow(Color.Black, 1));
                sb.Add(drawCall);

                drawCall.Text = String.Format("Player 2: {0:00}", Scores[1]);
                drawCall.Position.X = GraphicsDevice.Viewport.Width - 16 - sb.Measure(ref drawCall).X;

                sb.Add(drawCall.Shadow(Color.Black, 1));
                sb.Add(drawCall);
            }

            // The first stage of our frame involves selecting the trail buffer as our render target (note that it's layer 0)
            SetRenderTargetBatch.AddNew(frame, 0, 0, TrailBuffer, null);
            if (FirstFrame) {
                // If it's the first time we've rendered, we erase the trail buffer since it could contain anything
                ClearBatch.AddNew(frame, 1, Color.Black, Materials.Clear);
                FirstFrame = false;
            } else {
                // Otherwise, we fade out the contents of the trail buffer
                using (var gb = PrimitiveBatch<VertexPositionColor>.New(frame, 1, Materials.SubtractiveGeometry)) {
                    Primitives.FilledQuad(
                        gb, 
                        new Bounds(Vector2.Zero, new Vector2(GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height)), 
                        new Color(12, 12, 12)
                    );
                }
            }
            // After the trail buffer has been updated, we turn it off and begin rendering to the framebuffer. Note layer 3.
            SetRenderTargetBatch.AddNew(frame, 3, 0, null, DefaultDepthStencilBuffer);
        }
    }

    public class Playfield {
        public Bounds Bounds;
    }

    public class Ball {
        public Playfield Playfield;
        public Vector2 Position;
        public Vector2 Velocity;
        public float Radius;

        public void Update (Paddle[] paddles) {
            Vector2 surfaceNormal;
            Vector2 orientation = Vector2.Normalize(Velocity);
            var oldEdge = Position + (Radius * orientation);
            var newEdge = oldEdge + Velocity;

            // Check to see if the ball is going to collide with a paddle or with the edges of the playfield
            foreach (var poly in new[] { 
                Polygon.FromBounds(paddles[0].Bounds),
                Polygon.FromBounds(paddles[1].Bounds),
                Polygon.FromBounds(Playfield.Bounds),
            }) {
                var intersection = Geometry.LineIntersectPolygon(oldEdge, newEdge, poly, out surfaceNormal);

                if (!intersection.HasValue)
                    continue;

                // The ball is colliding, so we make it bounce off whatever it just hit
                Velocity = Vector2.Reflect(Velocity, surfaceNormal);

                return;
            }

            Position += Velocity;
        }
    }

    public class Paddle {
        public Playfield Playfield;
        public Bounds Bounds;
        public Vector2 Velocity;

        public void Update () {
            var newBounds = Bounds.Translate(Velocity);

            if (Playfield.Bounds.Contains(newBounds))
                Bounds = newBounds;
        }
    }

    public class PongMaterials : DefaultMaterialSet {
        public Material Trail;
        public Material SubtractiveGeometry;

        public PongMaterials (ContentManager content)
            : base(content) {

            // Set up various blending modes by changing render states
            var additiveBlend = new Action<DeviceManager>[] {
                (dm) => {
                    var rs = dm.Device.RenderState;
                    rs.DepthBufferEnable = false;
                    rs.AlphaBlendEnable = true;
                    rs.BlendFunction = BlendFunction.Add;
                    rs.SourceBlend = Blend.One;
                    rs.DestinationBlend = Blend.One;
                    rs.AlphaTestEnable = false;
                }
            };

            var subtractiveBlend = new Action<DeviceManager>[] {
                (dm) => {
                    var rs = dm.Device.RenderState;
                    rs.DepthBufferEnable = false;
                    rs.AlphaBlendEnable = true;
                    rs.BlendFunction = BlendFunction.ReverseSubtract;
                    rs.SourceBlend = Blend.One;
                    rs.DestinationBlend = Blend.One;
                    rs.AlphaTestEnable = false;
                }
            };

            var alphaBlend = new Action<DeviceManager>[] {
                (dm) => {
                    var rs = dm.Device.RenderState;
                    rs.DepthBufferEnable = false;
                    rs.AlphaBlendEnable = true;
                    rs.BlendFunction = BlendFunction.Add;
                    rs.SourceBlend = Blend.SourceAlpha;
                    rs.DestinationBlend = Blend.InverseSourceAlpha;
                    rs.AlphaTestEnable = false;
                }
            };

            // Replace the default materials with ones that set up our custom render states
            ScreenSpaceBitmap = new DelegateMaterial(
                base.ScreenSpaceBitmap,
                alphaBlend,
                null
            );

            ScreenSpaceGeometry = new DelegateMaterial(
                base.ScreenSpaceGeometry,
                alphaBlend,
                null
            );

            // Create a couple custom materials
            Trail = new DelegateMaterial(
                base.ScreenSpaceBitmap,
                additiveBlend,
                null
            );

            SubtractiveGeometry = new DelegateMaterial(
                base.ScreenSpaceGeometry,
                subtractiveBlend,
                null
            );
        }
    }
}