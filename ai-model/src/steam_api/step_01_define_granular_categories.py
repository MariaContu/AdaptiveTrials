from src.steam_api.config import (
    GRANULAR_CATEGORY_TAGS,
)


def validate_granular_categories() -> None:
    """Valida a estrutura das categorias granulares."""

    print("\n" + "=" * 60)
    print("Validação das categorias granulares")
    print("=" * 60)

    granular_names: set[str] = set()

    for macro_category, subcategories in (
        GRANULAR_CATEGORY_TAGS.items()
    ):
        print(
            f"\nMacrocategoria: {macro_category}"
        )

        if not subcategories:
            raise ValueError(
                f"A macrocategoria {macro_category} "
                "não possui subcategorias."
            )

        for subcategory, tags in (
            subcategories.items()
        ):
            if subcategory in granular_names:
                raise ValueError(
                    "Subcategoria duplicada: "
                    f"{subcategory}"
                )

            granular_names.add(subcategory)

            if not tags:
                raise ValueError(
                    f"A subcategoria {subcategory} "
                    "não possui tags."
                )

            print(
                f"- {subcategory}: "
                f"{', '.join(tags)}"
            )

    print(
        f"\nMacrocategorias: "
        f"{len(GRANULAR_CATEGORY_TAGS)}"
    )

    print(
        f"Subcategorias: "
        f"{len(granular_names)}"
    )

    print(
        "\nCategorias granulares "
        "validadas com sucesso."
    )


def main() -> None:
    validate_granular_categories()


if __name__ == "__main__":
    main()