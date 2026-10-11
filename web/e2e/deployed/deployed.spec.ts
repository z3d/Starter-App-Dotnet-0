import { expect, test } from "@playwright/test";
import { deleteProductsNamed, result, signInAtKeycloak, users } from "../lib/fixtures";

test("signing in goes through the identity provider and a write goes through nginx to the API", async ({ page }) => {
  await page.goto("/");
  await page.waitForURL(/\/realms\/starterapp\/protocol\/openid-connect\/auth/);
  await signInAtKeycloak(page, users.dev);
  await page.waitForURL(/\/$/);
  await expect(page.getByRole("heading", { level: 1, name: "StarterApp" })).toBeVisible();

  const name = await page.getByTestId("product-name").inputValue();
  try {
    await page.getByRole("button", { name: "Create the product" }).click();
    await expect(result(page, "product")).toContainText("POST /api/v1/products\n201");
  } finally {
    await deleteProductsNamed(name);
  }
});
