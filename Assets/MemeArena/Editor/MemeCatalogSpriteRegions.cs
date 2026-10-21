using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MemeArenaEditor
{
    // Alpha analysis only: source art is preserved. Output rectangles are ordinary sprite import data.
    public static class MemeCatalogSpriteRegions
    {
        sealed class Component
        {
            public int left, right, bottom, top, pixels;
        }

        public static RectInt[] Detect(Texture2D image, IEnumerable<int> requiredCells)
        {
            int width = image.width;
            int height = image.height;
            var pixels = image.GetPixels32();
            if (!pixels.Any(pixel => pixel.a == 0))
                throw new InvalidOperationException("Source sheet needs a transparent background: " + image.name);
            var seen = new bool[pixels.Length];
            var queue = new int[pixels.Length];
            var groups = Enumerable.Range(0, 16).Select(index => new List<Component>()).ToArray();
            for (int index = 0; index < pixels.Length; index++)
            {
                if (seen[index] || pixels[index].a < 96) continue;
                int read = 0;
                int write = 1;
                queue[0] = index;
                seen[index] = true;
                var region = new Component { left = width, bottom = height };
                while (read < write)
                {
                    int point = queue[read++];
                    int x = point % width;
                    int y = point / width;
                    region.left = Mathf.Min(region.left, x);
                    region.right = Mathf.Max(region.right, x);
                    region.bottom = Mathf.Min(region.bottom, y);
                    region.top = Mathf.Max(region.top, y);
                    for (int neighbor = 0; neighbor < 4; neighbor++)
                    {
                        int nx = x + (neighbor == 0 ? -1 : neighbor == 1 ? 1 : 0);
                        int ny = y + (neighbor == 2 ? -1 : neighbor == 3 ? 1 : 0);
                        if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        int candidate = ny * width + nx;
                        if (seen[candidate] || pixels[candidate].a < 96) continue;
                        seen[candidate] = true;
                        queue[write++] = candidate;
                    }
                }
                region.pixels = write;
                if (write < 200) continue;
                int column = Mathf.Clamp((region.left + region.right) * 2 / width, 0, 3);
                int row = 3 - Mathf.Clamp((region.bottom + region.top) * 2 / height, 0, 3);
                groups[row * 4 + column].Add(region);
            }
            var result = new RectInt[16];
            foreach (int cell in requiredCells.Distinct())
            {
                if (cell < 0 || cell >= 16) throw new ArgumentOutOfRangeException(nameof(requiredCells));
                var components = groups[cell];
                if (components.Count == 0 || components.Max(component => component.pixels) < 1000)
                    throw new InvalidOperationException("Missing or touching character in " + image.name + " cell " + cell);
                int left = Mathf.Max(0, components.Min(component => component.left) - 4);
                int bottom = Mathf.Max(0, components.Min(component => component.bottom) - 4);
                int right = Mathf.Min(width, components.Max(component => component.right) + 5);
                int top = Mathf.Min(height, components.Max(component => component.top) + 5);
                if (right - left > width * .39f || top - bottom > height * .39f)
                    throw new InvalidOperationException("Ambiguous character bounds in " + image.name + " cell " + cell);
                result[cell] = new RectInt(left, bottom, right - left, top - bottom);
            }
            return result;
        }
    }
}
