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
        
        # 1. Исправляем пустые results
        if not recipe.get("results"):
            if "results" in props and props["results"]:
                recipe["results"] = props["results"]
            elif "result" in props and props["result"]:
                recipe["results"] = [{"name": props["result"], "amount": 1, "type": "item"}]
            elif "normal" in props and "result" in props["normal"]:
                recipe["results"] = [{"name": props["normal"]["result"], "amount": 1, "type": "item"}]
                
        # 2. Исправляем пустые ingredients
        if not recipe.get("ingredients"):
            if "ingredients" in props and props["ingredients"]:
                recipe["ingredients"] = props["ingredients"]
            elif "normal" in props and "ingredients" in props["normal"]:
                recipe["ingredients"] = props["normal"]["ingredients"]
                
    return recipes

def main():
    # Пути к вашим файлам
    recipes_path = 'recipes.json'
    items_path = 'items.json'
    
    # Загрузка данных
    with open(recipes_path, 'r', encoding='utf-8') as f:
        recipes = json.load(f)
    with open(items_path, 'r', encoding='utf-8') as f:
        items = json.load(f)
        
    print("Очистка от лишних пробелов...")
    recipes = strip_spaces(recipes)
    items = strip_spaces(items)
    
    print("Нормализация структуры рецептов...")
    recipes = fix_recipes_structure(recipes)
    
    # Сохранение очищенных файлов
    with open('recipes_cleaned.json', 'w', encoding='utf-8') as f:
        json.dump(recipes, f, ensure_ascii=False, indent=2)
    with open('items_cleaned.json', 'w', encoding='utf-8') as f:
        json.dump(items, f, ensure_ascii=False, indent=2)
        
    print("Готово! Созданы файлы recipes_cleaned.json и items_cleaned.json")

if __name__ == "__main__":
    main()
