#!/usr/bin/env python3
"""Independent P0 arithmetic checks only; NOT C# build, SOLIDWORKS, or production tests."""
import math

def even(span, margin, n):
    if n < 1 or span <= 0 or 2 * margin >= span:
        raise ValueError("Invalid span/margin/count")
    return [span / 2] if n == 1 else [margin + (span - 2 * margin) * i / (n - 1) for i in range(n)]

assert len(even(2250, 50, 7)) == 7
assert math.isclose(even(2250, 50, 7)[1] - even(2250, 50, 7)[0], 358.33333333333)
assert len(even(1450, 150.9, 4)) == 4
assert 7 * 4 == 28 and 2 * 7 * 4 == 56
assert len(even(780, 0, 5)) == 5
assert 5 * 2 == 10 and 2 * 5 * 2 == 20
assert math.isclose(1.6 + .2, 1.8)
assert math.isclose(1.6 + 0, 1.6)
assert even(100, 0, 1) == [50]
try:
    even(100, 60, 3)
    raise AssertionError("Expected invalid margins")
except ValueError:
    pass
print("PASS: Kadai P0 layout / measured example clearance formulas only")
