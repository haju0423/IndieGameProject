using UnityEngine;

public sealed partial class Play2FingerRig
{
    // Each row is one digit's base, bend and tip in the imported wrist's bind space.
    // The thumb comes first, followed by the long digits in order across the hand.
    private static bool TryGetHands(string mesh, out Finger[] left, out Finger[] right)
    {
        switch (mesh)
        {
            case "tripo_node_1527849b": // Cat
                left = Hand(Millimeters(707, 571, -417),
                    Millimeters(24, 34, -35), Millimeters(22, 46, -45), Millimeters(15, 54, -47),
                    Millimeters(-27, 32, -29), Millimeters(-30, 59, -35), Millimeters(-31, 78, -32),
                    Millimeters(-25, 26, 0), Millimeters(-22, 60, -3), Millimeters(-23, 78, -1),
                    Millimeters(-14, 19, 23), Millimeters(-11, 51, 26), Millimeters(-11, 70, 30),
                    Millimeters(1, 14, 41), Millimeters(7, 40, 48), Millimeters(10, 64, 50));
                right = Hand(Millimeters(-844, 386, -371),
                    Millimeters(-31, 21, -28), Millimeters(-29, 39, -42), Millimeters(-26, 54, -50),
                    Millimeters(17, 28, -42), Millimeters(16, 60, -52), Millimeters(5, 88, -49),
                    Millimeters(23, 30, -18), Millimeters(30, 60, -23), Millimeters(5, 93, -21),
                    Millimeters(24, 31, 11), Millimeters(34, 60, 8), Millimeters(8, 92, 8),
                    Millimeters(16, 28, 35), Millimeters(15, 60, 37), Millimeters(2, 88, 35));
                return true;
            case "tripo_node_5afea4ce": // Cow
                left = Hand(Millimeters(717, 331, -613),
                    Millimeters(37, 23, -34), Millimeters(35, 38, -51), Millimeters(31, 49, -58),
                    Millimeters(-16, 36, -25), Millimeters(-21, 60, -39), Millimeters(-18, 82, -43),
                    Millimeters(-8, 30, 23), Millimeters(-9, 58, 12), Millimeters(-9, 79, -2),
                    Millimeters(-6, 8, 38), Millimeters(1, 24, 53), Millimeters(7, 44, 58));
                right = Hand(Millimeters(-620, 284, -731),
                    Millimeters(-25, 18, -40), Millimeters(-20, 30, -53), Millimeters(-11, 39, -64),
                    Millimeters(25, 32, -22), Millimeters(30, 62, -45), Millimeters(27, 77, -46),
                    Millimeters(9, 32, 24), Millimeters(8, 59, 20), Millimeters(15, 79, -6),
                    Millimeters(6, 10, 42), Millimeters(1, 33, 54), Millimeters(-2, 57, 54));
                return true;
            case "tripo_node_e21e8167": // Fox
                left = Hand(Millimeters(851, 287, -439),
                    Millimeters(23, 13, -18), Millimeters(26, 29, -33), Millimeters(24, 40, -41),
                    Millimeters(-18, 30, -18), Millimeters(-17, 56, -32), Millimeters(-13, 76, -31),
                    Millimeters(-21, 32, 10), Millimeters(-18, 61, 8), Millimeters(-15, 79, 5),
                    Millimeters(-15, 27, 39), Millimeters(-13, 53, 41), Millimeters(-10, 75, 38));
                right = Hand(Millimeters(-720, 561, -407),
                    Millimeters(-12, 19, -32), Millimeters(-8, 36, -44), Millimeters(-4, 48, -53),
                    Millimeters(30, 31, -22), Millimeters(33, 62, -31), Millimeters(32, 82, -26),
                    Millimeters(24, 25, 4), Millimeters(25, 54, 6), Millimeters(24, 78, 7),
                    Millimeters(9, 18, 28), Millimeters(12, 43, 33), Millimeters(8, 69, 39));
                return true;
            case "tripo_node_6e123013": // Owl
                left = Hand(Millimeters(857, 250, 451),
                    Millimeters(37, 15, 3), Millimeters(55, 40, -4), Millimeters(69, 59, -2),
                    Millimeters(10, 43, -25), Millimeters(26, 76, -30), Millimeters(26, 104, -23),
                    Millimeters(-24, 45, -11), Millimeters(-15, 80, -7), Millimeters(-12, 111, -3),
                    Millimeters(-35, 39, 7), Millimeters(-45, 68, 10), Millimeters(-45, 98, 21));
                right = Hand(Millimeters(-87, 326, 941),
                    Millimeters(-27, 25, -2), Millimeters(-45, 50, -4), Millimeters(-55, 63, -9),
                    Millimeters(-10, 48, -35), Millimeters(-10, 75, -43), Millimeters(-13, 104, -36),
                    Millimeters(23, 42, -27), Millimeters(31, 78, -24), Millimeters(27, 107, -14),
                    Millimeters(50, 34, -6), Millimeters(57, 64, -3), Millimeters(60, 96, 10));
                return true;
            default:
                left = right = null;
                return false;
        }
    }

    private static Vector3 Millimeters(float x, float y, float z) => new Vector3(x, y, z) * 0.001f;

    private static Finger[] Hand(Vector3 palm, params Vector3[] points)
    {
        var fingers = new Finger[points.Length / 3];
        for (int i = 0; i < fingers.Length; i++)
        {
            int start = i * 3;
            Vector3 root = points[start], bend = points[start + 1], tip = points[start + 2];
            Vector3 curl = i == 0 ? points[4] - bend : palm;
            fingers[i] = new Finger
            {
                Base = root, Bend = bend, Tip = tip, Thumb = i == 0,
                BaseAxis = Vector3.Cross(bend - root, curl).normalized,
                BendAxis = Vector3.Cross(tip - bend, curl).normalized
            };
        }
        return fingers;
    }
}
