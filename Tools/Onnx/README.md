# Basketball ONNX export

This folder contains offline authoring tools only. Python is not used by the Unity runtime.

From the Unity project root:

```powershell
uv run --with "numpy>=1.26,<3" --with "onnx>=1.17,<2" `
  Tools/Onnx/export_basketball_onnx.py `
  --source Assets/AI4AnimationRemake/Models/BasketballModel.Legacy.asset `
  --output Assets/AI4AnimationRemake/Resources/Models/BasketballMoEBatch3.onnx `
  --batch-size 3
```

The exporter preserves the `864 -> 588` neural contract and writes the original normalization,
Gating/Softmax, eight-way dynamic parameter blending, ELU and denormalization into a fixed
three-agent ONNX graph. The imported graph packs each agent's 588 outputs and 8 gating weights
into one 596-float readback row to avoid a second GPU-to-CPU transfer. It does not train,
quantize or alter any model input/output channel used by the recurrent simulation.
