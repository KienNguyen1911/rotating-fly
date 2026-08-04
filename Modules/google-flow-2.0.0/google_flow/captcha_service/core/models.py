from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime
from typing import Any

from pydantic import BaseModel, Field


class CaptchaConfig(BaseModel):
    id: int = 1
    captcha_method: str = "browser"
    browser_proxy_enabled: bool = False
    browser_proxy_url: str | None = None
    browser_count: int = 1
    personal_project_pool_size: int = 4
    personal_max_resident_tabs: int = 5
    personal_idle_tab_ttl_seconds: int = 600
    created_at: datetime | None = None
    updated_at: datetime | None = None


class ServiceApiKey(BaseModel):
    id: int
    name: str
    key_prefix: str
    enabled: bool = True
    quota_remaining: int | None = None
    quota_used: int = 0
    created_at: datetime | None = None
    updated_at: datetime | None = None
    last_used_at: datetime | None = None


class SolveRequest(BaseModel):
    project_id: str = Field(min_length=1)
    action: str = "IMAGE_GENERATION"
    token_id: int | None = None


class SolveResponse(BaseModel):
    success: bool = True
    session_id: str
    token: str
    fingerprint: dict[str, Any] | None = None
    node_name: str
    expires_in_seconds: int = 1200


class PrefillRequest(BaseModel):
    project_id: str = Field(min_length=1)
    action: str = "IMAGE_GENERATION"
    token_id: int | None = None


class FinishRequest(BaseModel):
    status: str = "success"


class ErrorRequest(BaseModel):
    error_reason: str = "upstream_error"


class CustomScoreRequest(BaseModel):
    website_url: str = "https://antcpt.com/score_detector/"
    website_key: str = "6LcR_okUAAAAAPYrPe-HK_0RULO1aZM15ENyM-Mf"
    verify_url: str = "https://antcpt.com/score_detector/verify.php"
    action: str = "homepage"
    enterprise: bool = False


class CustomTokenRequest(BaseModel):
    website_url: str = Field(min_length=1)
    website_key: str = Field(min_length=1)
    action: str = "homepage"
    enterprise: bool = False
    captcha_type: str = "recaptcha_v3"
    is_invisible: bool = True


class LoginRequest(BaseModel):
    username: str
    password: str


class PortalRegisterRequest(BaseModel):
    username: str = Field(min_length=3, max_length=60)
    password: str = Field(min_length=6, max_length=120)
    register_location: str = Field(min_length=1, max_length=120)
    display_name: str | None = Field(default=None, min_length=1, max_length=120)


class PortalRedeemRequest(BaseModel):
    code: str = Field(min_length=1, max_length=120)


class PortalUserUpdateRequest(BaseModel):
    username: str | None = Field(default=None, min_length=3, max_length=60)
    enabled: bool | None = None
    display_name: str | None = Field(default=None, min_length=1, max_length=120)
    quota_remaining_delta: int | None = None
    quota_remaining: int | None = Field(default=None, ge=0)
    quota_used: int | None = Field(default=None, ge=0)
    new_password: str | None = Field(default=None, min_length=6, max_length=120)


class BatchPortalUserDeleteRequest(BaseModel):
    user_ids: list[int] = Field(min_length=1, max_length=500)


class PortalUserApiKeyCreateRequest(BaseModel):
    name: str = Field(min_length=1, max_length=100)


class PortalUserApiKeyUpdateRequest(BaseModel):
    name: str | None = Field(default=None, min_length=1, max_length=100)
    enabled: bool | None = None


class PortalCdkBatchCreateRequest(BaseModel):
    count: int = Field(default=10, ge=1, le=500)
    quota_times: int = Field(default=1, ge=1, le=2147483647)
    prefix: str | None = Field(default="CDK", max_length=20)
    note: str | None = Field(default=None, max_length=200)


class UpdateCdkRequest(BaseModel):
    enabled: bool | None = None


class CreateApiKeyRequest(BaseModel):
    name: str = Field(min_length=1, max_length=100)
    quota_remaining: int | None = Field(default=None, ge=0)


class UpdateApiKeyRequest(BaseModel):
    name: str | None = Field(default=None, min_length=1, max_length=100)
    enabled: bool | None = None
    quota_remaining: int | None = Field(default=None, ge=0)


class UpdateCaptchaConfigRequest(BaseModel):
    captcha_method: str = "browser"
    browser_proxy_enabled: bool = False
    browser_proxy_url: str | None = None
    browser_count: int = Field(default=1, ge=1)
    personal_project_pool_size: int = Field(default=4, ge=1, le=50)
    personal_max_resident_tabs: int = Field(default=5, ge=1, le=50)
    personal_idle_tab_ttl_seconds: int = Field(default=600, ge=60)


class UpdateAdminCredentialsRequest(BaseModel):
    current_password: str = Field(min_length=1)
    new_username: str | None = Field(default=None, min_length=1, max_length=120)
    new_password: str | None = Field(default=None, min_length=6, max_length=120)


class UpdateSystemConfigRequest(BaseModel):
    server: dict[str, Any] | None = None
    storage: dict[str, Any] | None = None
    admin: dict[str, Any] | None = None
    portal: dict[str, Any] | None = None
    captcha: dict[str, Any] | None = None
    log: dict[str, Any] | None = None
    cluster: dict[str, Any] | None = None


class ClusterRegisterRequest(BaseModel):
    node_name: str = Field(min_length=1, max_length=120)
    base_url: str = Field(min_length=1)
    node_api_key: str = Field(min_length=1)
    weight: int = Field(default=100, ge=1)
    max_concurrency: int = Field(default=1, ge=1)
    browser_count: int = Field(default=1, ge=1)
    node_max_concurrency: int = Field(default=1, ge=1)
    effective_capacity: int = Field(default=1, ge=1)
    active_sessions: int = Field(default=0, ge=0)
    cached_sessions: int = Field(default=0, ge=0)
    standby_token_count: int = Field(default=0, ge=0)
    standby_bucket_signatures: list[str] = Field(default_factory=list, max_length=256)
    healthy: bool = True


class ClusterHeartbeatRequest(BaseModel):
    node_name: str = Field(min_length=1, max_length=120)
    base_url: str = Field(min_length=1)
    max_concurrency: int = Field(default=1, ge=1)
    browser_count: int = Field(default=1, ge=1)
    node_max_concurrency: int = Field(default=1, ge=1)
    effective_capacity: int = Field(default=1, ge=1)
    active_sessions: int = Field(default=0, ge=0)
    cached_sessions: int = Field(default=0, ge=0)
    standby_token_count: int = Field(default=0, ge=0)
    standby_bucket_signatures: list[str] = Field(default_factory=list, max_length=256)
    healthy: bool = True


class ClusterNodeUpdateRequest(BaseModel):
    enabled: bool | None = None
    weight: int | None = Field(default=None, ge=1)


class ClusterNodeLogClearRequest(BaseModel):
    scopes: list[str] = Field(min_length=1, max_length=4)


@dataclass
class SessionRecord:
    session_id: str
    browser_id: int
    api_key_id: int
    project_id: str
    action: str
    status: str = "pending"
    created_at: datetime = field(default_factory=datetime.utcnow)
    finished_at: datetime | None = None
    error_reason: str | None = None
