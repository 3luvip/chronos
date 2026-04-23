using Chronos.Core.Domain.Map;
using Godot;

namespace Map
{
    /// <summary>
    /// Stateless draw helpers for tile and decoration rendering.
    /// All methods are pure functions on <see cref="CanvasItem"/> — no shared state.
    ///
    /// Texture filter note:
    ///   Pixel-perfect rendering requires <c>TextureFilter = Nearest</c> set on the
    ///   <see cref="CanvasItem"/> node (i.e. <see cref="MapRenderer"/>).
    ///   Calling <c>tex.Set("texture_filter", ...)</c> on a <see cref="Texture2D"/>
    ///   resource has no effect on draw calls.
    /// </summary>
    public static class TileDrawHelper
    {
        /// <summary>Canonical tile display size in pixels.</summary>
        public const int DISPLAY_TILE_SIZE = TileMapData.TileSize;

        // ── Spritesheet drawing ───────────────────────────────────────────────────

        /// <summary>
        /// Draws a single tile from a spritesheet, always scaled to
        /// <see cref="DISPLAY_TILE_SIZE"/> × <see cref="DISPLAY_TILE_SIZE"/> on screen.
        /// </summary>
        public static void DrawTileFromSheet(CanvasItem canvas, TileSheetInfo sheet,
                                             int frameIndex, int screenX, int screenY)
        {
            if (frameIndex < 0) return;

            int column = frameIndex % sheet.Columns;
            int row    = frameIndex / sheet.Columns;

            var sourceRect = new Rect2(
                column * sheet.TilePixelSize,
                row    * sheet.TilePixelSize,
                sheet.TilePixelSize,
                sheet.TilePixelSize);

            var destRect = new Rect2(screenX, screenY, DISPLAY_TILE_SIZE, DISPLAY_TILE_SIZE);
            canvas.DrawTextureRectRegion(sheet.Sheet, destRect, sourceRect);
        }

        /// <summary>
        /// Draws a partial region of a spritesheet tile (used for slope rendering).
        /// </summary>
        public static void DrawTileRegionFromSheet(CanvasItem canvas, TileSheetInfo sheet,
                                                   int frameIndex, int screenX, int screenY,
                                                   int regionWidth, int regionHeight)
        {
            if (frameIndex < 0) return;

            int column = frameIndex % sheet.Columns;
            int row    = frameIndex / sheet.Columns;

            var sourceRect = new Rect2(
                column * sheet.TilePixelSize,
                row    * sheet.TilePixelSize,
                regionWidth, regionHeight);

            var destRect = new Rect2(screenX, screenY, regionWidth, regionHeight);
            canvas.DrawTextureRectRegion(sheet.Sheet, destRect, sourceRect);
        }

        // ── Individual texture drawing ────────────────────────────────────────────

        /// <summary>
        /// Draws a full texture, scaling it to exactly
        /// <see cref="DISPLAY_TILE_SIZE"/> × <see cref="DISPLAY_TILE_SIZE"/> on screen.
        /// Source PNG may be any size (e.g. 96 × 96 becomes 32 × 32).
        /// </summary>
        public static void DrawTileFromTexture(CanvasItem canvas, Texture2D texture,
                                               int screenX, int screenY)
        {
            if (texture == null) return;
            var destRect = new Rect2(screenX, screenY, DISPLAY_TILE_SIZE, DISPLAY_TILE_SIZE);
            canvas.DrawTextureRect(texture, destRect, false);
        }

        /// <summary>
        /// Draws a partial region of a texture, maintaining correct source-to-dest
        /// scaling when the source texture is not DISPLAY_TILE_SIZE pixels wide.
        /// Used for slope (T_DOWN_1_PIXEL) rendering.
        /// </summary>
        public static void DrawTileRegionFromTexture(CanvasItem canvas, Texture2D texture,
                                                     int screenX, int screenY,
                                                     int regionWidth, int regionHeight)
        {
            if (texture == null) return;

            float scaleX = (float)texture.GetWidth()  / DISPLAY_TILE_SIZE;
            float scaleY = (float)texture.GetHeight() / DISPLAY_TILE_SIZE;

            var sourceRect = new Rect2(0, 0, regionWidth * scaleX, regionHeight * scaleY);
            var destRect   = new Rect2(screenX, screenY, regionWidth, regionHeight);
            canvas.DrawTextureRectRegion(texture, destRect, sourceRect);
        }

        // ── Water animation drawing ───────────────────────────────────────────────

        /// <summary>
        /// Draws one frame from a vertically-arranged two-frame water animation strip.
        /// The strip must contain exactly 2 equal-height frames stacked vertically.
        /// </summary>
        public static void DrawWaterAnimationFrame(CanvasItem canvas, Texture2D texture,
                                                   int screenX, int screenY, int animationFrame)
        {
            if (texture == null) return;

            int frameHeight = texture.GetHeight() / 2;

            var sourceRect = new Rect2(0, animationFrame * frameHeight,
                                       texture.GetWidth(), frameHeight);
            var destRect   = new Rect2(screenX, screenY, DISPLAY_TILE_SIZE, DISPLAY_TILE_SIZE);
            canvas.DrawTextureRectRegion(texture, destRect, sourceRect);
        }

        // ── Decoration texture drawing ────────────────────────────────────────────

        /// <summary>
        /// Draws a decoration texture at its native size (no scaling).
        /// Used for background items where pixel dimensions are authored correctly.
        /// </summary>
        public static void DrawDecorationTexture(CanvasItem canvas, Texture2D texture,
                                                 int screenX, int screenY)
        {
            if (texture == null) return;
            canvas.DrawTexture(texture, new Vector2(screenX, screenY));
        }

        /// <summary>
        /// Draws a decoration texture scaled uniformly around its top-left corner.
        /// Scale 1.0 = native size, 0.5 = half, 2.0 = double.
        /// </summary>
        public static void DrawDecorationTextureScaled(CanvasItem canvas, Texture2D texture,
                                                       int screenX, int screenY, float scale)
        {
            if (texture == null) return;
            if (scale == 1.0f) { DrawDecorationTexture(canvas, texture, screenX, screenY); return; }

            int dstW = (int)(texture.GetWidth()  * scale);
            int dstH = (int)(texture.GetHeight() * scale);
            var destRect = new Rect2(screenX, screenY, dstW, dstH);
            canvas.DrawTextureRect(texture, destRect, tile: false);
        }

        /// <summary>
        /// Draws a decoration texture horizontally flipped, at its native size.
        /// Implements the flip by applying a negative-X scale transform,
        /// then restoring the identity transform after drawing.
        /// </summary>
        public static void DrawDecorationTextureFlippedHorizontal(CanvasItem canvas,
                                                                   Texture2D texture,
                                                                   int screenX, int screenY)
        {
            if (texture == null) return;

            int textureWidth = texture.GetWidth();

            // Negative X scale flips; offset by textureWidth so pivot is the left edge.
            canvas.DrawSetTransformMatrix(
                new Transform2D(-1, 0, 0, 1, screenX + textureWidth, screenY));
            canvas.DrawTexture(texture, Vector2.Zero);
            canvas.DrawSetTransformMatrix(Transform2D.Identity);
        }

        /// <summary>
        /// Draws a decoration texture horizontally flipped and scaled.
        /// </summary>
        public static void DrawDecorationTextureFlippedHorizontalScaled(CanvasItem canvas,
                                                                         Texture2D texture,
                                                                         int screenX, int screenY,
                                                                         float scale)
        {
            if (texture == null) return;
            if (scale == 1.0f) { DrawDecorationTextureFlippedHorizontal(canvas, texture, screenX, screenY); return; }

            int dstW = (int)(texture.GetWidth()  * scale);
            int dstH = (int)(texture.GetHeight() * scale);

            // Flip around the scaled width
            canvas.DrawSetTransformMatrix(
                new Transform2D(-scale, 0, 0, scale, screenX + dstW, screenY));
            canvas.DrawTexture(texture, Vector2.Zero);
            canvas.DrawSetTransformMatrix(Transform2D.Identity);
        }
    }
}