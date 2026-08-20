using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SideQuest.BundleAnalyzer
{
    /// <summary>
    /// Flat, single-level squarified treemap layout (Bruls/Huizing/van Wijk) - the algorithm
    /// behind WinDirStat's block view. One block per item, sized proportionally to its weight,
    /// laid out to keep block aspect ratios close to square rather than thin slivers.
    /// </summary>
    public static class BlockView
    {
        public readonly struct Block<T>
        {
            public readonly T Item;
            public readonly Rect Rect;
            public Block(T item, Rect rect) { Item = item; Rect = rect; }
        }

        public static List<Block<T>> Layout<T>(Rect bounds, IReadOnlyList<T> items, Func<T, double> weight)
        {
            var result = new List<Block<T>>();
            if (bounds.width <= 0 || bounds.height <= 0 || items.Count == 0)
                return result;

            var sorted = items.Where(i => weight(i) > 0).OrderByDescending(weight).ToList();
            if (sorted.Count == 0)
                return result;

            double total = sorted.Sum(weight);
            double scale = (double)bounds.width * bounds.height / total;
            var areas = sorted.Select(i => weight(i) * scale).ToList();

            var rect = bounds;
            int index = 0;
            var row = new List<int>();

            while (index < sorted.Count)
            {
                if (rect.width <= 0.01f || rect.height <= 0.01f)
                    break; // out of space - remaining (tiny) items just don't get a visible block.

                double shortSide = Mathf.Min(rect.width, rect.height);
                var candidateRow = new List<int>(row) { index };

                bool takeIt = row.Count == 0 || Worse(candidateRow, areas, shortSide) <= Worse(row, areas, shortSide);
                if (takeIt)
                {
                    row.Add(index);
                    index++;
                }
                else
                {
                    rect = LayoutRow(sorted, areas, row, rect, result);
                    row.Clear();
                }
            }

            if (row.Count > 0 && rect.width > 0.01f && rect.height > 0.01f)
                LayoutRow(sorted, areas, row, rect, result);

            return result;
        }

        static double Worse(List<int> row, List<double> areas, double shortSide)
        {
            double sum = 0, maxA = double.NegativeInfinity, minA = double.PositiveInfinity;
            foreach (var i in row)
            {
                double a = areas[i];
                sum += a;
                if (a > maxA) maxA = a;
                if (a < minA) minA = a;
            }

            double s2 = shortSide * shortSide;
            double sum2 = sum * sum;
            return Math.Max(s2 * maxA / sum2, sum2 / (s2 * minA));
        }

        static Rect LayoutRow<T>(List<T> items, List<double> areas, List<int> row, Rect rect, List<Block<T>> result)
        {
            double rowArea = 0;
            foreach (var i in row) rowArea += areas[i];

            if (rect.width < rect.height)
            {
                double rowHeight = rowArea / rect.width;
                double x = rect.x;
                foreach (var i in row)
                {
                    double w = areas[i] / rowHeight;
                    result.Add(new Block<T>(items[i], new Rect((float)x, rect.y, (float)w, (float)rowHeight)));
                    x += w;
                }
                return new Rect(rect.x, rect.y + (float)rowHeight, rect.width, rect.height - (float)rowHeight);
            }
            else
            {
                double rowWidth = rowArea / rect.height;
                double y = rect.y;
                foreach (var i in row)
                {
                    double h = areas[i] / rowWidth;
                    result.Add(new Block<T>(items[i], new Rect(rect.x, (float)y, (float)rowWidth, (float)h)));
                    y += h;
                }
                return new Rect(rect.x + (float)rowWidth, rect.y, rect.width - (float)rowWidth, rect.height);
            }
        }
    }
}
