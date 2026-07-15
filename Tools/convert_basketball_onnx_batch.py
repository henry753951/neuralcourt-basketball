"""Resize the fixed Basketball MoE ONNX batch without changing its math.

The exported graph keeps the expert weights flattened and reshapes each
blended layer with a leading batch dimension.  Consequently, changing the
input/output metadata alone is insufficient: the three named reshape tensors
must use the requested batch as well.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import onnx
from onnx import numpy_helper


RESHAPE_INITIALIZERS = (
    "MoE/Layer0_weights_shape",
    "MoE/Layer1_weights_shape",
    "MoE/Layer2_weights_shape",
)


def set_leading_dimension(value_info: onnx.ValueInfoProto, batch_size: int) -> None:
    shape = value_info.type.tensor_type.shape
    if not shape.dim:
        raise ValueError(f"{value_info.name!r} has no tensor dimensions")
    shape.dim[0].ClearField("dim_param")
    shape.dim[0].dim_value = batch_size


def convert(source: Path, destination: Path, batch_size: int) -> None:
    model = onnx.load(source)
    if len(model.graph.input) != 1 or model.graph.input[0].name != "input":
        raise ValueError("Expected one graph input named 'input'.")
    if len(model.graph.output) != 1 or model.graph.output[0].name != "batch_output":
        raise ValueError("Expected one graph output named 'batch_output'.")

    set_leading_dimension(model.graph.input[0], batch_size)
    set_leading_dimension(model.graph.output[0], batch_size)

    initializers = {initializer.name: initializer for initializer in model.graph.initializer}
    for name in RESHAPE_INITIALIZERS:
        initializer = initializers.get(name)
        if initializer is None:
            raise ValueError(f"Missing required reshape initializer {name!r}.")
        shape = numpy_helper.to_array(initializer).astype(np.int64, copy=True)
        if shape.shape != (3,):
            raise ValueError(f"Unexpected {name!r} shape tensor: {shape.tolist()}")
        shape[0] = batch_size
        initializer.CopyFrom(numpy_helper.from_array(shape, name=name))

    model.graph.name = f"BasketballMoEBatch{batch_size}"
    model.doc_string = (
        f"AI4Animation Basketball MoE fixed batch {batch_size}; converted from "
        f"{source.name} without changing weights or operators."
    )
    onnx.checker.check_model(model)
    destination.parent.mkdir(parents=True, exist_ok=True)
    onnx.save(model, destination)

    verified = onnx.load(destination)
    onnx.checker.check_model(verified)
    input_batch = verified.graph.input[0].type.tensor_type.shape.dim[0].dim_value
    output_batch = verified.graph.output[0].type.tensor_type.shape.dim[0].dim_value
    if input_batch != batch_size or output_batch != batch_size:
        raise ValueError("Saved graph did not retain the requested batch dimensions.")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("source", type=Path)
    parser.add_argument("destination", type=Path)
    parser.add_argument("batch_size", type=int)
    args = parser.parse_args()
    if args.batch_size <= 0:
        raise ValueError("batch_size must be positive")
    convert(args.source, args.destination, args.batch_size)


if __name__ == "__main__":
    main()
