#!/usr/bin/env python3
"""Export the Unity BasketballModel legacy YAML asset to an exact ONNX MoE graph.

This is an offline authoring tool. Python is not used by the Unity runtime.
"""

from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
from typing import Dict

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper


INPUT_COUNT = 864
MAIN_COUNT = 734
GATING_COUNT = 130
OUTPUT_COUNT = 588
GATING_HIDDEN = 128
HIDDEN = 512
EXPERTS = 8
COMBINED_OUTPUT_COUNT = OUTPUT_COUNT + EXPERTS


EXPECTED_LENGTHS = {
    "Xmean": INPUT_COUNT,
    "Xstd": INPUT_COUNT,
    "Ymean": OUTPUT_COUNT,
    "Ystd": OUTPUT_COUNT,
    "wc000_w": GATING_HIDDEN * GATING_COUNT,
    "wc000_b": GATING_HIDDEN,
    "wc010_w": GATING_HIDDEN * GATING_HIDDEN,
    "wc010_b": GATING_HIDDEN,
    "wc020_w": EXPERTS * GATING_HIDDEN,
    "wc020_b": EXPERTS,
}

for expert in range(EXPERTS):
    EXPECTED_LENGTHS[f"wc10{expert}_w"] = HIDDEN * MAIN_COUNT
    EXPECTED_LENGTHS[f"wc10{expert}_b"] = HIDDEN
    EXPECTED_LENGTHS[f"wc11{expert}_w"] = HIDDEN * HIDDEN
    EXPECTED_LENGTHS[f"wc11{expert}_b"] = HIDDEN
    EXPECTED_LENGTHS[f"wc12{expert}_w"] = OUTPUT_COUNT * HIDDEN
    EXPECTED_LENGTHS[f"wc12{expert}_b"] = OUTPUT_COUNT


def parse_legacy_asset(path: Path) -> Dict[str, np.ndarray]:
    buffers: Dict[str, list[float]] = {}
    current_id: str | None = None

    with path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, start=1):
            if line.startswith("  - ID: "):
                current_id = line[len("  - ID: ") :].strip()
                if current_id in buffers:
                    raise ValueError(f"Duplicate buffer '{current_id}' at line {line_number}.")
                buffers[current_id] = []
            elif current_id is not None and line.startswith("    - "):
                buffers[current_id].append(float(line[len("    - ") :].strip()))

    if set(buffers) != set(EXPECTED_LENGTHS):
        missing = sorted(set(EXPECTED_LENGTHS) - set(buffers))
        extra = sorted(set(buffers) - set(EXPECTED_LENGTHS))
        raise ValueError(f"Unexpected model buffers. Missing={missing}, extra={extra}")

    result: Dict[str, np.ndarray] = {}
    for name, expected_length in EXPECTED_LENGTHS.items():
        values = np.asarray(buffers[name], dtype=np.float32)
        if values.size != expected_length:
            raise ValueError(
                f"Buffer '{name}' contains {values.size} floats; expected {expected_length}."
            )
        if not np.isfinite(values).all():
            raise ValueError(f"Buffer '{name}' contains a non-finite value.")
        result[name] = values
    return result


class GraphBuilder:
    def __init__(self) -> None:
        self.nodes: list[onnx.NodeProto] = []
        self.initializers: list[onnx.TensorProto] = []

    def initializer(self, name: str, values: np.ndarray) -> str:
        self.initializers.append(numpy_helper.from_array(values, name=name))
        return name

    def constant_i64(self, name: str, values: list[int]) -> str:
        return self.initializer(name, np.asarray(values, dtype=np.int64))

    def node(
        self,
        op_type: str,
        inputs: list[str],
        outputs: list[str],
        name: str,
        **attributes: object,
    ) -> str:
        self.nodes.append(
            helper.make_node(op_type, inputs, outputs, name=name, **attributes)
        )
        return outputs[0]


def dense(
    graph: GraphBuilder,
    input_name: str,
    weight_name: str,
    bias_name: str,
    prefix: str,
    elu: bool,
) -> str:
    matmul = graph.node(
        "MatMul", [input_name, weight_name], [f"{prefix}_matmul"], f"{prefix}/MatMul"
    )
    added = graph.node(
        "Add", [matmul, bias_name], [f"{prefix}_preactivation"], f"{prefix}/Add"
    )
    if not elu:
        return added
    return graph.node("Elu", [added], [f"{prefix}_elu"], f"{prefix}/ELU", alpha=1.0)


def blend_layer(
    graph: GraphBuilder,
    activation: str,
    gating: str,
    expert_weights: str,
    expert_bias: str,
    batch_size: int,
    input_count: int,
    output_count: int,
    prefix: str,
    elu: bool,
) -> str:
    flat = graph.node(
        "MatMul",
        [gating, expert_weights],
        [f"{prefix}_weights_flat"],
        f"{prefix}/BlendWeights",
    )
    shape = graph.constant_i64(
        f"{prefix}_weights_shape", [batch_size, output_count, input_count]
    )
    blended_weights = graph.node(
        "Reshape",
        [flat, shape],
        [f"{prefix}_weights"],
        f"{prefix}/ReshapeWeights",
    )
    axes = graph.constant_i64(f"{prefix}_unsqueeze_axes", [2])
    activation_column = graph.node(
        "Unsqueeze",
        [activation, axes],
        [f"{prefix}_input_column"],
        f"{prefix}/UnsqueezeInput",
    )
    multiplied = graph.node(
        "MatMul",
        [blended_weights, activation_column],
        [f"{prefix}_column"],
        f"{prefix}/DenseMatMul",
    )
    squeezed = graph.node(
        "Squeeze",
        [multiplied, axes],
        [f"{prefix}_dense"],
        f"{prefix}/SqueezeOutput",
    )
    blended_bias = graph.node(
        "MatMul",
        [gating, expert_bias],
        [f"{prefix}_bias"],
        f"{prefix}/BlendBias",
    )
    added = graph.node(
        "Add",
        [squeezed, blended_bias],
        [f"{prefix}_preactivation"],
        f"{prefix}/Add",
    )
    if not elu:
        return added
    return graph.node("Elu", [added], [f"{prefix}_elu"], f"{prefix}/ELU", alpha=1.0)


def export_model(buffers: Dict[str, np.ndarray], output: Path, batch_size: int) -> None:
    graph = GraphBuilder()

    def matrix(name: str, rows: int, columns: int) -> np.ndarray:
        # The C# backend stores dense matrices as [output, input]. ONNX MatMul
        # consumes [input, output], hence this one explicit transpose.
        return buffers[name].reshape(rows, columns).T.copy()

    xmean = graph.initializer("Xmean", buffers["Xmean"])
    xstd = graph.initializer("Xstd", buffers["Xstd"])
    ymean = graph.initializer("Ymean", buffers["Ymean"])
    ystd = graph.initializer("Ystd", buffers["Ystd"])

    gating_w0 = graph.initializer(
        "gating_w0", matrix("wc000_w", GATING_HIDDEN, GATING_COUNT)
    )
    gating_b0 = graph.initializer("gating_b0", buffers["wc000_b"])
    gating_w1 = graph.initializer(
        "gating_w1", matrix("wc010_w", GATING_HIDDEN, GATING_HIDDEN)
    )
    gating_b1 = graph.initializer("gating_b1", buffers["wc010_b"])
    gating_w2 = graph.initializer(
        "gating_w2", matrix("wc020_w", EXPERTS, GATING_HIDDEN)
    )
    gating_b2 = graph.initializer("gating_b2", buffers["wc020_b"])

    def expert_weights(prefix: str, rows: int, columns: int) -> np.ndarray:
        return np.stack(
            [buffers[f"{prefix}{expert}_w"].reshape(rows, columns) for expert in range(EXPERTS)]
        ).reshape(EXPERTS, rows * columns)

    def expert_bias(prefix: str) -> np.ndarray:
        return np.stack([buffers[f"{prefix}{expert}_b"] for expert in range(EXPERTS)])

    expert_w0 = graph.initializer(
        "expert_w0", expert_weights("wc10", HIDDEN, MAIN_COUNT)
    )
    expert_b0 = graph.initializer("expert_b0", expert_bias("wc10"))
    expert_w1 = graph.initializer(
        "expert_w1", expert_weights("wc11", HIDDEN, HIDDEN)
    )
    expert_b1 = graph.initializer("expert_b1", expert_bias("wc11"))
    expert_w2 = graph.initializer(
        "expert_w2", expert_weights("wc12", OUTPUT_COUNT, HIDDEN)
    )
    expert_b2 = graph.initializer("expert_b2", expert_bias("wc12"))

    normalized_numerator = graph.node(
        "Sub", ["input", xmean], ["normalized_numerator"], "Normalize/Sub"
    )
    normalized = graph.node(
        "Div", [normalized_numerator, xstd], ["normalized"], "Normalize/Div"
    )

    slice_axes = graph.constant_i64("slice_axes", [1])
    slice_steps = graph.constant_i64("slice_steps", [1])
    main = graph.node(
        "Slice",
        [
            normalized,
            graph.constant_i64("main_starts", [0]),
            graph.constant_i64("main_ends", [MAIN_COUNT]),
            slice_axes,
            slice_steps,
        ],
        ["main_features"],
        "Features/Main",
    )
    gating_input = graph.node(
        "Slice",
        [
            normalized,
            graph.constant_i64("gating_starts", [MAIN_COUNT]),
            graph.constant_i64("gating_ends", [INPUT_COUNT]),
            slice_axes,
            slice_steps,
        ],
        ["gating_features"],
        "Features/Gating",
    )

    gating_h0 = dense(
        graph, gating_input, gating_w0, gating_b0, "Gating/Layer0", elu=True
    )
    gating_h1 = dense(
        graph, gating_h0, gating_w1, gating_b1, "Gating/Layer1", elu=True
    )
    gating_logits = dense(
        graph, gating_h1, gating_w2, gating_b2, "Gating/Layer2", elu=False
    )
    gating = graph.node(
        "Softmax",
        [gating_logits],
        ["gating_weights"],
        "Gating/Softmax",
        axis=1,
    )

    hidden0 = blend_layer(
        graph,
        main,
        gating,
        expert_w0,
        expert_b0,
        batch_size,
        MAIN_COUNT,
        HIDDEN,
        "MoE/Layer0",
        elu=True,
    )
    hidden1 = blend_layer(
        graph,
        hidden0,
        gating,
        expert_w1,
        expert_b1,
        batch_size,
        HIDDEN,
        HIDDEN,
        "MoE/Layer1",
        elu=True,
    )
    normalized_output = blend_layer(
        graph,
        hidden1,
        gating,
        expert_w2,
        expert_b2,
        batch_size,
        HIDDEN,
        OUTPUT_COUNT,
        "MoE/Layer2",
        elu=False,
    )
    scaled_output = graph.node(
        "Mul", [normalized_output, ystd], ["scaled_output"], "Denormalize/Mul"
    )
    output_values = graph.node(
        "Add", [scaled_output, ymean], ["output_values"], "Denormalize/Add"
    )
    graph.node(
        "Concat",
        [output_values, gating],
        ["batch_output"],
        "Outputs/Pack",
        axis=1,
    )

    model_graph = helper.make_graph(
        graph.nodes,
        "AI4Animation Basketball 2020 MoE",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, [batch_size, INPUT_COUNT])],
        [
            helper.make_tensor_value_info(
                "batch_output",
                TensorProto.FLOAT,
                [batch_size, COMBINED_OUTPUT_COUNT],
            )
        ],
        graph.initializers,
    )
    model = helper.make_model(
        model_graph,
        producer_name="CrowdEyes AI4Animation Unity 6 Remake",
        producer_version="1",
        opset_imports=[helper.make_opsetid("", 17)],
    )
    model.ir_version = 9
    model.metadata_props.add(key="ai4animation.input_count", value=str(INPUT_COUNT))
    model.metadata_props.add(key="ai4animation.output_count", value=str(OUTPUT_COUNT))
    model.metadata_props.add(
        key="ai4animation.combined_output_count", value=str(COMBINED_OUTPUT_COUNT)
    )
    model.metadata_props.add(key="ai4animation.expert_count", value=str(EXPERTS))
    model.metadata_props.add(key="ai4animation.batch_size", value=str(batch_size))
    model.metadata_props.add(
        key="ai4animation.source_sha256",
        value=hashlib.sha256(
            b"".join(buffers[name].tobytes() for name in sorted(buffers))
        ).hexdigest(),
    )

    onnx.checker.check_model(model, full_check=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    onnx.save_model(model, output)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--batch-size", type=int, default=3)
    args = parser.parse_args()
    if args.batch_size < 1:
        raise ValueError("--batch-size must be positive.")

    buffers = parse_legacy_asset(args.source)
    export_model(buffers, args.output, args.batch_size)
    print(f"Exported {args.output} ({args.output.stat().st_size:,} bytes).")


if __name__ == "__main__":
    main()
