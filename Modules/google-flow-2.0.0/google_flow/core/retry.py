"""
Retry strategy with exponential backoff and jitter.

Provides a reusable :func:`with_retry` decorator and a
:class:`RetryPolicy` configuration object so every callsite gets
consistent, configurable retry behaviour.
"""

from __future__ import annotations

import asyncio
import functools
import random
from typing import (
    TYPE_CHECKING,
    Any,
    TypeVar,
)

from google_flow.constants import (
    DEFAULT_MAX_RETRIES,
    DEFAULT_RETRY_BASE_DELAY,
    DEFAULT_RETRY_MAX_DELAY,
)
from google_flow.logging import get_logger

if TYPE_CHECKING:
    from collections.abc import Awaitable, Callable, Sequence

logger = get_logger(__name__)

T = TypeVar("T")


class RetryPolicy:
    """Configurable retry policy with exponential backoff.

    Parameters
    ----------
    max_retries:
        Maximum number of retry attempts (excluding the initial call).
    base_delay:
        Initial delay in seconds before the first retry.
    max_delay:
        Upper bound on the delay between retries.
    retryable_exceptions:
        Exception types that trigger a retry.  By default only
        :class:`Exception` is retried — subclass checks apply.
    jitter:
        If *True*, add random jitter (±25 %) to the delay.
    """

    def __init__(
        self,
        *,
        max_retries: int = DEFAULT_MAX_RETRIES,
        base_delay: float = DEFAULT_RETRY_BASE_DELAY,
        max_delay: float = DEFAULT_RETRY_MAX_DELAY,
        retryable_exceptions: Sequence[type[BaseException]] = (Exception,),
        jitter: bool = True,
    ) -> None:
        self.max_retries = max(0, max_retries)
        self.base_delay = base_delay
        self.max_delay = max_delay
        self.retryable_exceptions = tuple(retryable_exceptions)
        self.jitter = jitter

    def compute_delay(self, attempt: int) -> float:
        """Return the sleep duration for the given *attempt* number (0-based)."""
        delay = min(self.base_delay * (2 ** attempt), self.max_delay)
        if self.jitter:
            delay *= 0.75 + random.random() * 0.5  # ±25 %
        return delay

    def is_retryable(self, exc: BaseException) -> bool:
        return isinstance(exc, self.retryable_exceptions)


async def execute_with_retry(
    func: Callable[..., Awaitable[T]],
    *args: Any,
    policy: RetryPolicy | None = None,
    on_retry: Callable[[int, BaseException, float], Awaitable[None] | None] | None = None,
    **kwargs: Any,
) -> T:
    """Execute *func* with automatic retries governed by *policy*.

    Parameters
    ----------
    func:
        An async callable to execute.
    policy:
        Retry policy.  Uses a sensible default when *None*.
    on_retry:
        Optional callback ``(attempt, exception, delay)`` invoked
        before each retry sleep.  May be sync or async.
    """
    pol = policy or RetryPolicy()
    last_exc: BaseException | None = None

    for attempt in range(1 + pol.max_retries):
        try:
            return await func(*args, **kwargs)
        except BaseException as exc:
            last_exc = exc
            is_last = attempt >= pol.max_retries
            if not pol.is_retryable(exc) or is_last:
                raise

            delay = pol.compute_delay(attempt)
            logger.warning(
                "Attempt %d/%d failed (%s), retrying in %.1fs …",
                attempt + 1,
                pol.max_retries + 1,
                exc,
                delay,
            )
            if on_retry is not None:
                result = on_retry(attempt, exc, delay)
                if asyncio.iscoroutine(result):
                    await result

            await asyncio.sleep(delay)

    # Should be unreachable, but satisfy type checkers
    assert last_exc is not None
    raise last_exc


def with_retry(
    policy: RetryPolicy | None = None,
) -> Callable[[Callable[..., Awaitable[T]]], Callable[..., Awaitable[T]]]:
    """Decorator version of :func:`execute_with_retry`.

    Usage::

        @with_retry(RetryPolicy(max_retries=3))
        async def do_work():
            ...
    """

    def decorator(func: Callable[..., Awaitable[T]]) -> Callable[..., Awaitable[T]]:
        @functools.wraps(func)
        async def wrapper(*args: Any, **kwargs: Any) -> T:
            return await execute_with_retry(func, *args, policy=policy, **kwargs)

        return wrapper

    return decorator
