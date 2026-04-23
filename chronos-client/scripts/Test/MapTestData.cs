using System.Collections.Generic;
using Chronos.Core.Domain.Map;

public static class MapTestData
{
    public static readonly byte[,] Layout = new byte[21, 59]
    {
        // col: 0  1  2  3  4  5  6  7  8  9 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31 32 33 34 35 36 37 38
        /* r0 */ {39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39, 39 },
        /* r1 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r2 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r3 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r4 */ {39, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 ,0 ,0 ,0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 39 },
        /* r4 */ {39, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2 ,2 ,2 ,2, 2, 2, 2, 2, 2, 11, 11, 11, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,10,11,11,12, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 11, 11, 11, 0, 39 },
        /* r4 */ {39, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 ,6 ,6 ,6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 39 },
        /* r4 */ {39, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 ,6 ,6 ,6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 39 },
        /* r4 */ {39, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6 ,6 ,6 ,6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 6, 39 },
        /* r9 */ {39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39,39, 39 },
    };

    public static readonly IEnumerable<BackgroundItem> backgroundItems = new List<BackgroundItem>
    {
        {new BackgroundItem {Id = 1, ImageId = 84, WorldX = 171, WorldY = 546, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 2, ImageId = 84, WorldX = 416, WorldY = 600, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 3, ImageId = 84, WorldX = 208, WorldY = 574, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 4, ImageId = 84, WorldX = 15, WorldY = 546, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 5, ImageId = 85, WorldX = 247, WorldY = 548, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 6, ImageId = 84, WorldX = 784, WorldY = 603, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 7, ImageId = 84, WorldX = 837, WorldY = 567, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 8, ImageId = 84, WorldX = 521, WorldY = 585, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 9, ImageId = 85, WorldX = 588, WorldY = 588, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 10, ImageId = 84, WorldX = 665, WorldY = 546, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 11, ImageId = 86, WorldX = 91, WorldY = 558, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 12, ImageId = 87, WorldX = 72, WorldY = 592, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 13, ImageId = 86, WorldX = 441, WorldY = 561, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 14, ImageId = 88, WorldX = 717, WorldY = 587, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 15, ImageId = 88, WorldX = 1017, WorldY = 588, Layer = 2, Scale = 0.35f}},
        {new BackgroundItem {Id = 16, ImageId = 86, WorldX = 977, WorldY = 578, Layer = 2, Scale = 0.35f}},

        {new BackgroundItem {Id = 17, ImageId = 104, WorldX = 448, WorldY = 250, Layer = 4, Scale = 0.55f}},
        {new BackgroundItem {Id = 17, ImageId = 104, WorldX = 135, WorldY = 250, Layer = 4, Scale = 0.55f}},
        {new BackgroundItem {Id = 18, ImageId = 99, WorldX = 585, WorldY = 461, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 19, ImageId = 98, WorldX = 535, WorldY = 461, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 26, ImageId = 75, WorldX = 420, WorldY = 420, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 26, ImageId = 75, WorldX = 320, WorldY = 420, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 26, ImageId = 74, WorldX = 280, WorldY = 461, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 26, ImageId = 73, WorldX = 233, WorldY = 461, Layer = 4, Scale = 0.40f}},
        
        {new BackgroundItem {Id = 26, ImageId = 77, WorldX = 292, WorldY = 488, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 20, ImageId = 76, WorldX = 456, WorldY = 430, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 21, ImageId = 97, WorldX = 475, WorldY = 461, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 22, ImageId = 81, WorldX = 670, WorldY = 410, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 23, ImageId = 77, WorldX = 665, WorldY = 481, Layer = 4, Scale = 0.50f}},
        {new BackgroundItem {Id = 24, ImageId = 100,WorldX = 790, WorldY = 410, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 25, ImageId = 79, WorldX = 815, WorldY = 462, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 26, ImageId = 75, WorldX = 390, WorldY = 420, Layer = 4, Scale = 0.40f}},

        {new BackgroundItem {Id = 27, ImageId = 28, WorldX = 215, WorldY = 385, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 31, WorldX = 215, WorldY = 365, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 16, WorldX = 235, WorldY = 367, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 31, WorldX = 215, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 28, WorldX = 215, WorldY = 242, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 27, WorldX = 111, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 31, WorldX = 215, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 31, WorldX = 92, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 27, WorldX = -29, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 31, WorldX = -170, WorldY = 230, Layer = 4, Scale = 0.40f}},
        {new BackgroundItem {Id = 27, ImageId = 27, WorldX = -170, WorldY = 230, Layer = 4, Scale = 0.40f}},

    };
}