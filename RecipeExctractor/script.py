import json
import os

def strip_spaces(obj):
    """Рекурсивно удаляет лишние пробелы в началах и концах всех ключей и строк"""
    if isinstance(obj, dict):
        return {k.strip(): strip_spaces(v) for k, v in obj.items()}
    elif isinstance(obj, list):
        return [strip_spaces(elem) for elem in obj]
    elif isinstance(obj, str):
        return obj.strip()
    else:
        return obj

def fix_recipes_structure(recipes):
    """Переносит результаты и ингредиенты из properties на верхний уровень"""
    for recipe in recipes:
        props = recipe.get("properties", {})
        
        if not recipe.get("results"):
            if "results" in props and props["results"]:
                recipe["results"] = props["results"]
            elif "result" in props and props["result"]:
                recipe["results"] = [{"name": props["result"], "amount": 1, "type": "item"}]
            elif "normal" in props and "result" in props["normal"]:
                recipe["results"] = [{"name": props["normal"]["result"], "amount": 1, "type": "item"}]
                
        if not recipe.get("ingredients"):
            if "ingredients" in props and props["ingredients"]:
                recipe["ingredients"] = props["ingredients"]
            elif "normal" in props and "ingredients" in props["normal"]:
                recipe["ingredients"] = props["normal"]["ingredients"]
                
    return recipes

def main(base):
    recipes_path = os.path.join(base, 'recipes.json')
    items_path = os.path.join(base, 'items.json')
    
    recipes_out_path = os.path.join(base, 'recipes_cleaned.json')
    items_out_path = os.path.join(base, 'items_cleaned.json')
    
    # Проверка существования файлов (для отладки)
    if not os.path.exists(recipes_path):
        print(f"Файл не найден: {recipes_path}")
        print(f"Текущая рабочая директория: {os.getcwd()}")
        return
    if not os.path.exists(items_path):
        print(f"Файл не найден: {items_path}")
        return

    with open(recipes_path, 'r', encoding='utf-8') as f:
        recipes = json.load(f)
    with open(items_path, 'r', encoding='utf-8') as f:
        items = json.load(f)
        
    print("Очистка от лишних пробелов...")
    recipes = strip_spaces(recipes)
    items = strip_spaces(items)
    
    print("Нормализация структуры рецептов...")
    recipes = fix_recipes_structure(recipes)
    
    with open(recipes_out_path, 'w', encoding='utf-8') as f:
        json.dump(recipes, f, ensure_ascii=False, indent=2)
    with open(items_out_path, 'w', encoding='utf-8') as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
        
    print(f"Готово! Созданы файлы:\n- {recipes_out_path}\n- {items_out_path}")

if __name__ == "__main__":
    # Получаем папку, в которой лежит script.py (...\RecipeExctractor\RecipeExctractor)
    script_dir = os.path.dirname(os.path.abspath(__file__))
    
    # Спускаемся вниз: bin -> Debug -> net8.0 -> FactoroData
    # Обратите внимание: без "..", и добавлена папка "FactoroData"
    base = os.path.join(script_dir, "bin", "Debug", "net8.0", "FactorioData")
    
    main(base)
